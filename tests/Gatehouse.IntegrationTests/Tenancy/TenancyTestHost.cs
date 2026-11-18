using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tenancy;
using Gatehouse.Infrastructure;
using Gatehouse.Infrastructure.Persistence.Catalog;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.Infrastructure.Security;
using Gatehouse.Infrastructure.Tenancy;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Gatehouse.IntegrationTests.Tenancy;

/// <summary>A minimal host with catalog + tenancy registrations against a fresh catalog database.</summary>
internal sealed class TenancyTestHost : IAsyncDisposable
{
    public const string MasterKey = "integration-test-master-key-0123456789-abcdefghij";

    private readonly PostgresFixture _postgres;

    private TenancyTestHost(IHost host, PostgresFixture postgres)
    {
        Host = host;
        _postgres = postgres;
    }

    public IHost Host { get; }

    public IServiceProvider Services => Host.Services;

    public static async Task<TenancyTestHost> CreateAsync(PostgresFixture postgres, CancellationToken ct)
    {
        var catalogConnectionString = await postgres.CreateDatabaseAsync(PostgresFixture.UniqueName("catalog"), ct);

        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development,
            DisableDefaults = true,
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:catalog"] = catalogConnectionString,
            ["ConnectionStrings:postgres"] = postgres.ServerConnectionString,
            [AesGcmSecretProtector.MasterKeySetting] = MasterKey,
        });
        builder.AddGatehouseCatalog();
        builder.AddGatehouseTenancy();

        var host = builder.Build();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Database.MigrateAsync(ct);
        }

        return new TenancyTestHost(host, postgres);
    }

    /// <summary>Creates and migrates a tenant database of its own (unique name, so tests run in parallel), then registers the tenant in the catalog.</summary>
    /// <param name="template">A migrated database without open connections to clone instead of migrating a new one (many tenants).</param>
    public async Task<TenantId> AddTenantAsync(string slug, TenantStatus status, CancellationToken ct, string? template = null)
    {
        var id = TenantId.Create(slug).Value;
        var name = PostgresFixture.UniqueName("t_" + slug.Replace('-', '_'));
        string connectionString;
        if (template is not null)
        {
            await _postgres.CloneDatabaseAsync(template, name, ct);
            connectionString = _postgres.ConnectionStringFor(name);
        }
        else
        {
            connectionString = await _postgres.CreateDatabaseAsync(name, ct);
        }

        await using (var tenantDb = new TenantDbContext(new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options))
        {
            if (template is null)
            {
                await tenantDb.Database.MigrateAsync(ct);
            }
        }

        await using var scope = Services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();
        var tenant = Domain.Tenancy.Tenant.Create(
            id,
            slug,
            IdpKind.Oidc,
            protector.Protect(connectionString),
            DateTimeOffset.UtcNow).Value;
        if (status == TenantStatus.Suspended)
        {
            tenant.Suspend("test", DateTimeOffset.UtcNow);
        }

        catalog.Tenants.Add(tenant);
        await catalog.SaveChangesAsync(ct);
        return id;
    }

    public async ValueTask DisposeAsync()
    {
        if (Host is IAsyncDisposable asyncDisposable)
        {
            await asyncDisposable.DisposeAsync();
        }
        else
        {
            Host.Dispose();
        }
    }
}
