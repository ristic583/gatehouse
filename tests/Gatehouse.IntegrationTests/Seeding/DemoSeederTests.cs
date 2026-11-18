extern alias GatehouseApi;

using System.Net;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Credentials;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.Tenancy;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.Infrastructure.Seeding;
using Gatehouse.IntegrationTests.Infrastructure;
using Gatehouse.IntegrationTests.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Seeding;

public sealed class DemoSeederTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Nordlys_seed_creates_expected_areas_and_people()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await TenancyTestHost.CreateAsync(postgres, ct);
        var tenant = await host.AddTenantAsync("nordlys", TenantStatus.Active, ct);
        await host.Services.GetRequiredService<DemoSeeder>().SeedAsync(tenant, ct);

        await AssertCountsAsync(host, tenant, areas: 10, people: 60, pacs: 2, mappings: 9, ct);
    }

    [Fact]
    public async Task Bricks_seed_creates_expected_areas_and_people()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await TenancyTestHost.CreateAsync(postgres, ct);
        var tenant = await host.AddTenantAsync("bricks", TenantStatus.Active, ct);
        await host.Services.GetRequiredService<DemoSeeder>().SeedAsync(tenant, ct);

        await AssertCountsAsync(host, tenant, areas: 4, people: 60, pacs: 1, mappings: 4, ct);
    }

    [Fact]
    public async Task Seeding_twice_is_idempotent()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await TenancyTestHost.CreateAsync(postgres, ct);
        var tenant = await host.AddTenantAsync("nordlys", TenantStatus.Active, ct);
        var seeder = host.Services.GetRequiredService<DemoSeeder>();
        await seeder.SeedAsync(tenant, ct);
        await seeder.SeedAsync(tenant, ct);

        await AssertCountsAsync(host, tenant, areas: 10, people: 60, pacs: 2, mappings: 9, ct);
    }

    [Fact]
    public async Task Every_area_has_an_owner()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await TenancyTestHost.CreateAsync(postgres, ct);
        foreach (var slug in new[] { "nordlys", "bricks" })
        {
            var tenant = await host.AddTenantAsync(slug, TenantStatus.Active, ct);
            await host.Services.GetRequiredService<DemoSeeder>().SeedAsync(tenant, ct);
            await using var scope = await host.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(tenant, ct);
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var areas = await db.Areas.AsNoTracking().ToListAsync(ct);
            Assert.NotEmpty(areas);
            Assert.All(areas, a => Assert.NotEmpty(a.Owners));
        }
    }

    [Fact]
    public async Task Seeds_one_active_card_per_person()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await TenancyTestHost.CreateAsync(postgres, ct);
        var tenant = await host.AddTenantAsync("nordlys", TenantStatus.Active, ct);
        await host.Services.GetRequiredService<DemoSeeder>().SeedAsync(tenant, ct);
        await using var scope = await host.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(tenant, ct);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var cards = await db.Cards.AsNoTracking().ToListAsync(ct);
        Assert.Equal(await db.Persons.CountAsync(ct), cards.Count);
        Assert.All(cards, c => Assert.Equal(CardState.Active, c.State));
        Assert.Equal(cards.Count, cards.Select(c => c.CardNumber).Distinct().Count());
    }

    [Fact]
    public async Task Reset_applies_mode_override()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await TenancyTestHost.CreateAsync(postgres, ct);
        var tenant = await host.AddTenantAsync("nordlys", TenantStatus.Active, ct);
        var seeder = host.Services.GetRequiredService<DemoSeeder>();
        await seeder.SeedAsync(tenant, ct);
        await seeder.ResetAccessDataAsync(tenant, FulfillmentMode.Manual, ct);
        await using var scope = await host.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(tenant, ct);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        Assert.All(await db.PacsInstances.AsNoTracking().ToListAsync(ct), p => Assert.Equal(FulfillmentMode.Manual, p.FulfillmentMode));
    }

    [Fact]
    public async Task Reset_without_override_restores_seed_modes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await TenancyTestHost.CreateAsync(postgres, ct);
        var tenant = await host.AddTenantAsync("nordlys", TenantStatus.Active, ct);
        var seeder = host.Services.GetRequiredService<DemoSeeder>();
        await seeder.SeedAsync(tenant, ct);
        await seeder.ResetAccessDataAsync(tenant, FulfillmentMode.Manual, ct);
        await seeder.ResetAccessDataAsync(tenant, null, ct);
        await using var scope = await host.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(tenant, ct);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        foreach (var p in await db.PacsInstances.AsNoTracking().ToListAsync(ct))
        {
            Assert.Equal(p.Name is "HQ-Copenhagen" or "Warehouse-Aarhus" ? FulfillmentMode.Connector : FulfillmentMode.Manual, p.FulfillmentMode);
        }
    }

    [Fact]
    public async Task Reset_access_restores_seeded_entitlements()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await TenancyTestHost.CreateAsync(postgres, ct);
        var tenant = await host.AddTenantAsync("nordlys", TenantStatus.Active, ct);
        var seeder = host.Services.GetRequiredService<DemoSeeder>();
        await seeder.SeedAsync(tenant, ct);

        await using (var scope = await host.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(tenant, ct))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            await db.Fulfillments.ExecuteDeleteAsync(ct);
            await db.Entitlements.ExecuteDeleteAsync(ct);
        }

        await seeder.ResetAccessDataAsync(tenant, null, ct);

        await using (var scope = await host.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(tenant, ct))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var anna = await db.Persons.SingleAsync(p => p.Email == "anna.larsen@nordlys.test", ct);
            var entitlements = await db.Entitlements.AsNoTracking().Where(e => e.PersonId == anna.Id).ToListAsync(ct);
            Assert.Equal(5, entitlements.Count);
            Assert.Equal(3, entitlements.Count(e => e.Status == EntitlementStatus.Approved));
            Assert.Equal(2, entitlements.Count(e => e.Status == EntitlementStatus.PendingApproval));
            Assert.Equal(5, await db.Entitlements.CountAsync(ct));
            Assert.Equal(3, await db.Fulfillments.CountAsync(f => f.Status == FulfillmentStatus.Confirmed, ct));

            var vault = await db.Areas.SingleAsync(a => a.Code == "CPH-VAULT", ct);
            var vaultRequest = entitlements.Single(e => e.AreaId == vault.Id);
            Assert.Equal(ApproverKind.Manager, vaultRequest.Steps.Single(s => s.Status == ApprovalStepStatus.Pending).ApproverKind);
            Assert.Equal(anna.ManagerId, vaultRequest.Steps[0].AssignedApproverId);
        }
    }

    [Fact]
    public async Task Dev_reset_endpoint_is_not_mapped_by_default()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new GatehouseApiFactory(postgres);
        await factory.InitializeAsync();
        using var client = factory.CreateClientFor("test-anna", "anna.larsen@nordlys.test", "nordlys");

        using var response = await client.PostAsync(new Uri("/api/v1/dev/reset-demo-access", UriKind.Relative), null, ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task AssertCountsAsync(TenancyTestHost host, TenantId tenant, int areas, int people, int pacs, int mappings, CancellationToken ct)
    {
        await using var scope = await host.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(tenant, ct);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        Assert.Equal(areas, await db.Areas.CountAsync(ct));
        Assert.Equal(people, await db.Persons.CountAsync(ct));
        Assert.Equal(pacs, await db.PacsInstances.CountAsync(ct));
        Assert.Equal(mappings, await db.AccessLevelMappings.CountAsync(ct));
        Assert.Equal(areas, await db.Doors.CountAsync(ct));
        Assert.Equal(areas * 2, await db.Readers.CountAsync(ct)); // one In and one Out reader per door (M9-09)
    }
}
