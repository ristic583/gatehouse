extern alias GatehouseApi;

using Gatehouse.Application.Platform;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tenancy;
using Gatehouse.Infrastructure.Connectors;
using Gatehouse.Infrastructure.Persistence;
using Gatehouse.Infrastructure.Persistence.Catalog;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.Infrastructure.Security;
using Gatehouse.Infrastructure.Seeding;
using Gatehouse.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gatehouse.IntegrationTests.Infrastructure;

/// <summary>
/// The real API against its own copy of the demo databases (<see cref="DemoTemplate"/>): a catalog with tenants
/// <c>nordlys</c>, <c>bricks</c> and suspended <c>frozen</c>, both demo tenants seeded (<see cref="DemoSeeder"/>) with demo access
/// data reset and no identity links, and <see cref="TestAuthHandler"/> instead of JWT. The copies are dropped on dispose,
/// so tests never restore shared data.
/// </summary>
public sealed class GatehouseApiFactory(PostgresFixture postgres) : WebApplicationFactory<GatehouseApi::Program>, IAsyncLifetime
{
    public const string MasterKey = "api-factory-test-master-key-0123456789-abcdefghij";

    private readonly List<string> _databases = [];
    private string _catalogConnectionString = string.Empty;
    private MessagingDriver? _messaging;

    private GatehouseApiFactory(PostgresFixture postgres, DemoTemplate building)
        : this(postgres)
    {
        _catalogConnectionString = postgres.ConnectionStringFor(building.Catalog);
    }

    public string CatalogConnectionString => _catalogConnectionString;

    public async ValueTask InitializeAsync()
    {
        var template = await DemoTemplate.GetAsync(postgres);
        _catalogConnectionString = await template.CloneAsync(postgres, _databases, CancellationToken.None);
    }

    /// <summary>Creates and seeds the <see cref="DemoTemplate"/> databases through a real API host (once per run).</summary>
    internal static async Task<DemoTemplate> BuildDemoTemplateAsync(PostgresFixture postgres)
    {
        var template = DemoTemplate.NewNames();
        foreach (var database in template.Databases)
        {
            await postgres.CreateDatabaseAsync(database);
        }

        await using (var factory = new GatehouseApiFactory(postgres, template))
        {
            await factory.SeedTemplateAsync(template);
        }

        await postgres.TerminateConnectionsAsync(template.Databases);
        return template;
    }

    private async Task SeedTemplateAsync(DemoTemplate template)
    {
        var ct = CancellationToken.None;
        var migrations = Services.GetRequiredService<MigrationRunner>();
        await migrations.MigrateCatalogAsync(ct);

        // Once in the template, as the CLI does at setup, instead of an RSA-3072 CA per test on first use.
        await using (var caScope = Services.CreateAsyncScope())
        {
            await caScope.ServiceProvider.GetRequiredService<ConnectorCertificateAuthority>().EnsureCreatedAsync(ct);
        }

        await using var scope = Services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();
        foreach (var (slug, displayName, idp) in new[] { ("nordlys", "Nordlys A/S", IdpKind.Oidc), ("bricks", "Bricks GmbH", IdpKind.Saml), ("frozen", "Frozen Ltd", IdpKind.Local) })
        {
            var id = new TenantId(slug);
            var tenant = Gatehouse.Domain.Tenancy.Tenant.Create(id, displayName, idp, protector.Protect(postgres.ConnectionStringFor(template.Tenants[slug])), DateTimeOffset.UtcNow).Value;
            if (slug == "frozen")
            {
                tenant.Suspend("test", DateTimeOffset.UtcNow);
            }

            catalog.Tenants.Add(tenant);
            await catalog.SaveChangesAsync(ct);
            var migrated = await migrations.MigrateTenantAsync(id, ct);
            if (!migrated.Succeeded)
            {
                throw new InvalidOperationException($"Template tenant {slug} did not migrate: {migrated.Error}");
            }
        }

        var seeder = Services.GetRequiredService<DemoSeeder>();
        var scopes = Services.GetRequiredService<ITenantScopeFactory>();
        foreach (var tenant in new[] { new TenantId("nordlys"), new TenantId("bricks") })
        {
            await seeder.SeedAsync(tenant, ct);
            await seeder.ResetAccessDataAsync(tenant, Gatehouse.Domain.Fulfillment.FulfillmentMode.Manual, ct);
            await using var tenantScope = await scopes.CreateScopeAsync(tenant, ct);
            await tenantScope.ServiceProvider.GetRequiredService<TenantDbContext>().Persons
                .ExecuteUpdateAsync(p => p.SetProperty(x => x.IdentitySubject, (string?)null), ct);
        }
    }

    public HttpClient CreateClientFor(DemoUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return CreateClientFor(user.Subject, user.Email, user.Tenant);
    }

    /// <summary>A scope bound to a tenant database, for arranging or asserting data directly.</summary>
    public Task<AsyncServiceScope> CreateTenantScopeAsync(string tenant) =>
        Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(new TenantId(tenant), CancellationToken.None);

    public HttpClient CreateClientFor(string sub, string email, string? tenant)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, sub);
        client.DefaultRequestHeaders.Add(TestAuthHandler.EmailHeader, email);
        if (tenant is not null)
        {
            client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, tenant);
        }

        return client;
    }

    /// <summary>A client authenticated only by the <c>PlatformBearer</c> scheme (<see cref="TestPlatformAuthHandler"/>).</summary>
    public HttpClient CreatePlatformClient(string sub, string email)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add(TestPlatformAuthHandler.SubHeader, sub);
        client.DefaultRequestHeaders.Add(TestPlatformAuthHandler.EmailHeader, email);
        return client;
    }

    /// <summary>Relays the tenant outbox through the in-process Workers pipeline and waits for the consumers (follow-up tasks etc.).</summary>
    public async Task DrainMessagesAsync(string tenant)
    {
        var messaging = await MessagingAsync();
        await messaging.DrainAsync(new TenantId(tenant), CancellationToken.None);
    }

    /// <summary>The in-process Workers pipeline (created on first use).</summary>
    public async Task<MessagingDriver> MessagingAsync() =>
        _messaging ??= await MessagingDriver.CreateAsync(Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>());

    public override async ValueTask DisposeAsync()
    {
        if (_messaging is not null)
        {
            await _messaging.DisposeAsync();
        }

        await base.DisposeAsync();
        foreach (var database in _databases)
        {
            await postgres.DropDatabaseAsync(database);
        }

        GC.SuppressFinalize(this);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:postgres", postgres.ServerConnectionString);
        builder.UseSetting("ConnectionStrings:catalog", _catalogConnectionString);
        builder.UseSetting(AesGcmSecretProtector.MasterKeySetting, MasterKey);
        builder.UseSetting("Gatehouse:Auth:Authority", "http://localhost:8080/realms/gatehouse");
        builder.UseSetting("Gatehouse:Auth:Audience", "gatehouse-api");
        builder.UseSetting("Storage:Provider", "memory");

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<FakeIdentityProvisioner>();
            services.Replace(ServiceDescriptor.Singleton<Application.Tenancy.IIdentityProvisioner>(sp => sp.GetRequiredService<FakeIdentityProvisioner>()));
            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultScheme = TestAuthHandler.SchemeName;
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
                options.DefaultForbidScheme = TestAuthHandler.SchemeName;
                options.SchemeMap[PlatformAuthentication.Scheme].HandlerType = typeof(TestPlatformAuthHandler);
            });
        });
    }
}

/// <summary>Named demo people (M2-08 seed). Subject is <c>test-{username}</c>.</summary>
public sealed record DemoUser(string Username, string Tenant)
{
    public static readonly DemoUser Anna = new("anna.larsen", "nordlys");
    public static readonly DemoUser Mikkel = new("mikkel.holm", "nordlys");
    public static readonly DemoUser Sofie = new("sofie.berg", "nordlys");
    public static readonly DemoUser Erik = new("erik.moller", "nordlys");
    public static readonly DemoUser Jonas = new("jonas.nielsen", "nordlys");
    public static readonly DemoUser Freja = new("freja.dahl", "nordlys");
    public static readonly DemoUser Oliver = new("oliver.kruse", "nordlys");
    public static readonly DemoUser Lena = new("lena.schmidt", "bricks");
    public static readonly DemoUser Max = new("max.weber", "bricks");
    public static readonly DemoUser Clara = new("clara.fischer", "bricks");
    public static readonly DemoUser Jan = new("jan.koch", "bricks");
    public static readonly DemoUser Paul = new("paul.wagner", "bricks");
    public static readonly DemoUser Mia = new("mia.becker", "bricks");
    public static readonly DemoUser Tom = new("tom.richter", "bricks");

    public string Subject => "test-" + Username;

    public string Email => $"{Username}@{Tenant}.test";
}
