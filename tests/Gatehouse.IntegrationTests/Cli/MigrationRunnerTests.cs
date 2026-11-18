using Gatehouse.Domain.Tenancy;
using Gatehouse.Infrastructure.Persistence;
using Gatehouse.Infrastructure.Persistence.Catalog;
using Gatehouse.Infrastructure.Tenancy;
using Gatehouse.IntegrationTests.Infrastructure;
using Gatehouse.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Cli;

public sealed class MigrationRunnerTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Migrate_all_migrates_every_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await TenancyTestHost.CreateAsync(postgres, ct);
        var first = TenantProvisioningServiceTests.NewTenantId("mig");
        var second = TenantProvisioningServiceTests.NewTenantId("mig");
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<TenantProvisioningService>();
            await service.CreateTenantAsync(first, "First", IdpKind.Oidc, ct);
            await service.CreateTenantAsync(second, "Second", IdpKind.Saml, ct);
        }

        var results = await host.Services.GetRequiredService<MigrationRunner>().MigrateAllAsync(ct);

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.True(r.Succeeded, r.Error));
        Assert.Equal(
            new[] { first, second }.Select(t => t.Value).Order(StringComparer.Ordinal),
            results.Select(r => r.TenantId.Value));
    }

    [Fact]
    public async Task Bootstrap_is_idempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await TenancyTestHost.CreateAsync(postgres, ct);
        var runner = host.Services.GetRequiredService<MigrationRunner>();
        var id = TenantProvisioningServiceTests.NewTenantId("boot");

        for (var run = 0; run < 2; run++)
        {
            // Same sequence as `gatehouse-cli bootstrap`: catalog, create-if-missing, migrate all.
            await runner.MigrateCatalogAsync(ct);
            await using (var scope = host.Services.CreateAsyncScope())
            {
                var created = await scope.ServiceProvider.GetRequiredService<TenantProvisioningService>()
                    .CreateTenantIfMissingAsync(id, "Boot Ltd", IdpKind.Oidc, ct);
                Assert.True(created.IsSuccess);
                Assert.Equal(run == 0, created.Value);
            }

            var results = await runner.MigrateAllAsync(ct);
            Assert.All(results, r => Assert.True(r.Succeeded, r.Error));
        }

        await using var check = host.Services.CreateAsyncScope();
        Assert.Equal(1, await check.ServiceProvider.GetRequiredService<CatalogDbContext>().Tenants.CountAsync(ct));
    }
}
