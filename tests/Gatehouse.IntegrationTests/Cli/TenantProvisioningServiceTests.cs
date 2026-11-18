using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tenancy;
using Gatehouse.Infrastructure.Persistence;
using Gatehouse.Infrastructure.Persistence.Catalog;
using Gatehouse.Infrastructure.Tenancy;
using Gatehouse.IntegrationTests.Infrastructure;
using Gatehouse.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Gatehouse.IntegrationTests.Cli;

public sealed class TenantProvisioningServiceTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Create_tenant_creates_database_and_catalog_row()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await TenancyTestHost.CreateAsync(postgres, ct);
        var id = NewTenantId("prov");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<TenantProvisioningService>()
                .CreateTenantAsync(id, "Provisioned Ltd", IdpKind.Saml, ct);
            Assert.True(result.IsSuccess);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var tenant = await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Tenants
                .AsNoTracking().SingleAsync(t => t.Id == id, ct);
            Assert.Equal("Provisioned Ltd", tenant.DisplayName);
            Assert.Equal(IdpKind.Saml, tenant.IdpKind);
            Assert.StartsWith("v1:", tenant.EncryptedConnectionString, StringComparison.Ordinal);
        }

        var provisioner = host.Services.GetRequiredService<TenantDatabaseProvisioner>();
        await using var connection = new NpgsqlConnection(provisioner.BuildConnectionString(id));
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM \"__EFMigrationsHistory\"";
        Assert.True((long)(await command.ExecuteScalarAsync(ct))! > 0);
    }

    [Fact]
    public async Task Creating_existing_tenant_returns_conflict()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await TenancyTestHost.CreateAsync(postgres, ct);
        var id = NewTenantId("dup");

        await using var scope = host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<TenantProvisioningService>();
        Assert.True((await service.CreateTenantAsync(id, "First", IdpKind.Oidc, ct)).IsSuccess);

        var second = await service.CreateTenantAsync(id, "Second", IdpKind.Oidc, ct);

        Assert.True(second.IsFailure);
        Assert.Equal("tenant.already_exists", second.Error.Code);
        Assert.Equal(ErrorKind.Conflict, second.Error.Kind);
    }

    internal static TenantId NewTenantId(string prefix) =>
        TenantId.Create(prefix + "-" + Guid.NewGuid().ToString("N")[..8]).Value;
}
