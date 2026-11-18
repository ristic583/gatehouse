using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.Tenancy;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.Infrastructure.Seeding;
using Gatehouse.IntegrationTests.Infrastructure;
using Gatehouse.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Seeding;

public sealed class CloudPacsSeedTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Warehouse_aarhus_is_connector_mode_with_access_groups()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await TenancyTestHost.CreateAsync(postgres, ct);
        var tenant = await host.AddTenantAsync("nordlys", TenantStatus.Active, ct);
        var seeder = host.Services.GetRequiredService<DemoSeeder>();
        await seeder.SeedAsync(tenant, ct);
        await seeder.ResetAccessDataAsync(tenant, null, ct);

        await using var scope = await host.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(tenant, ct);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var aarhus = await db.PacsInstances.AsNoTracking().SingleAsync(p => p.Name == "Warehouse-Aarhus", ct);
        Assert.Equal(PacsVendor.CloudPacs, aarhus.Vendor);
        Assert.Equal(FulfillmentMode.Connector, aarhus.FulfillmentMode);
        var refs = await RefsAsync(db, "Warehouse-Aarhus", ct);
        Assert.NotEmpty(refs);
        Assert.All(refs, r => Assert.StartsWith("AG-AAR-", r, StringComparison.Ordinal));
        Assert.Contains("AG-AAR-DOCK", refs);
        Assert.All(await RefsAsync(db, "HQ-Copenhagen", ct), r => Assert.StartsWith("AL-", r, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reseed_rewrites_legacy_refs_for_cloudpacs()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await TenancyTestHost.CreateAsync(postgres, ct);
        var tenant = await host.AddTenantAsync("nordlys", TenantStatus.Active, ct);
        var seeder = host.Services.GetRequiredService<DemoSeeder>();
        await seeder.SeedAsync(tenant, ct);
        await using (var scope = await host.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(tenant, ct))
        {
            // a tenant seeded before M6 carries AL- refs for the CloudPACS instance
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var aarhusId = (await db.PacsInstances.AsNoTracking().SingleAsync(p => p.Name == "Warehouse-Aarhus", ct)).Id;
            await db.Database.ExecuteSqlAsync(
                $"UPDATE access_level_mappings SET vendor_access_level_ref = 'AL-' || substr(vendor_access_level_ref, 4) WHERE pacs_instance_id = {aarhusId.Value}", ct);
            Assert.All(await RefsAsync(db, "Warehouse-Aarhus", ct), r => Assert.StartsWith("AL-", r, StringComparison.Ordinal));
        }

        await seeder.SeedAsync(tenant, ct);
        await seeder.SeedAsync(tenant, ct);

        await using (var scope = await host.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(tenant, ct))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var refs = await RefsAsync(db, "Warehouse-Aarhus", ct);
            Assert.All(refs, r => Assert.StartsWith("AG-AAR-", r, StringComparison.Ordinal));
            Assert.Contains("AG-AAR-DOCK", refs);
            Assert.All(await RefsAsync(db, "HQ-Copenhagen", ct), r => Assert.StartsWith("AL-", r, StringComparison.Ordinal));
        }
    }

    private static async Task<List<string>> RefsAsync(TenantDbContext db, string pacsName, CancellationToken ct)
    {
        var pacsId = (await db.PacsInstances.AsNoTracking().SingleAsync(p => p.Name == pacsName, ct)).Id;
        return await db.AccessLevelMappings.AsNoTracking().Where(m => m.PacsInstanceId == pacsId).Select(m => m.VendorAccessLevelRef).ToListAsync(ct);
    }
}
