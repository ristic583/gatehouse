using System.Net.Http.Json;
using Gatehouse.Application.Access.Dtos;
using Gatehouse.Application.Fulfillment;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Access;

public sealed class AccessMapEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatehouseApiFactory _factory = new(postgres);

    public ValueTask InitializeAsync() => _factory.InitializeAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    public static async Task<Guid> FloorIdAsync(GatehouseApiFactory factory, string tenant, string siteCode, int level)
    {
        await using var scope = await factory.CreateTenantScopeAsync(tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var site = await db.Sites.SingleAsync(s => s.Code == siteCode);
        return (await db.Floors.SingleAsync(f => f.SiteId == site.Id && f.Level == level)).Id.Value;
    }

    [Fact]
    public async Task Anna_sees_all_four_states_on_second_floor()
    {
        var map = await GetMapAsync(_factory.CreateClientFor(DemoUser.Anna));

        AssertArea(map, "Office Second", "HasAccess", "Confirmed");
        AssertArea(map, "Plant Room", "Unknown", "AreaNotMapped");
        AssertArea(map, "Server Room", "NoAccess", "NoEntitlement");
        AssertArea(map, "Vault", "Pending", "AwaitingApproval");
    }

    [Fact]
    public async Task Offline_connector_turns_mapped_areas_unknown()
    {
        await using var app = _factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddSingleton<IConnectorHealthReader>(new OfflineHealthReader())));
        using var client = app.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, DemoUser.Anna.Subject);
        client.DefaultRequestHeaders.Add(TestAuthHandler.EmailHeader, DemoUser.Anna.Email);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, DemoUser.Anna.Tenant);

        var map = await GetMapAsync(client);

        AssertArea(map, "Office Second", "Unknown", "ConnectorOffline");
        AssertArea(map, "Vault", "Unknown", "ConnectorOffline");
        AssertArea(map, "Plant Room", "Unknown", "AreaNotMapped");
        Assert.Equal(OfflineHealthReader.Since, map.Areas.Single(a => a.Name == "Vault").Since);
    }

    [Fact]
    public async Task Polygon_and_doors_are_returned()
    {
        var map = await GetMapAsync(_factory.CreateClientFor(DemoUser.Anna));

        Assert.Equal(1000, map.PlanWidth);
        Assert.Equal(600, map.PlanHeight);
        var server = map.Areas.Single(a => a.Code == "CPH-SERVER");
        Assert.Equal([new(540, 40), new(960, 40), new(960, 300), new(540, 300)], server.Polygon);
        var door = Assert.Single(server.Doors);
        Assert.Equal("Server Room door", door.Name);
        Assert.Equal((750d, 300d), (door.X, door.Y));
    }

    [Fact]
    public async Task Viewing_the_map_creates_no_entitlements()
    {
        int before;
        await using (var scope = await _factory.CreateTenantScopeAsync("nordlys"))
        {
            before = await scope.ServiceProvider.GetRequiredService<TenantDbContext>().Entitlements.CountAsync(TestContext.Current.CancellationToken);
        }

        await GetMapAsync(_factory.CreateClientFor(DemoUser.Anna));

        await using (var scope = await _factory.CreateTenantScopeAsync("nordlys"))
        {
            Assert.Equal(before, await scope.ServiceProvider.GetRequiredService<TenantDbContext>().Entitlements.CountAsync(TestContext.Current.CancellationToken));
        }
    }

    private static void AssertArea(FloorAccessMapDto map, string name, string state, string reason)
    {
        var area = map.Areas.Single(a => a.Name == name);
        Assert.Equal((state, reason), (area.State, area.Reason));
    }

    private async Task<FloorAccessMapDto> GetMapAsync(HttpClient client)
    {
        using (client)
        {
            var floorId = await FloorIdAsync(_factory, "nordlys", "CPH-HQ", 2);
            var map = await client.GetFromJsonAsync<FloorAccessMapDto>(
                new Uri($"/api/v1/floors/{floorId}/access-map", UriKind.Relative), TestContext.Current.CancellationToken);
            Assert.NotNull(map);
            return map;
        }
    }

    private sealed class OfflineHealthReader : IConnectorHealthReader
    {
        public static readonly DateTimeOffset Since = new(2026, 11, 9, 7, 0, 0, TimeSpan.Zero);

        public Task<IReadOnlyDictionary<PacsInstanceId, ConnectorHealth>> GetAsync(IReadOnlyCollection<PacsInstanceId> instanceIds, CancellationToken ct)
        {
            IReadOnlyDictionary<PacsInstanceId, ConnectorHealth> result = instanceIds.ToDictionary(id => id, _ => new ConnectorHealth(false, Since));
            return Task.FromResult(result);
        }
    }
}
