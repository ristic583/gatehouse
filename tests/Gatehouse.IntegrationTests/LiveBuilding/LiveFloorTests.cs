using System.Net;
using System.Net.Http.Json;
using Gatehouse.Application.Common.Messaging;
using Gatehouse.Application.Connectors.Messages;
using Gatehouse.Application.LiveBuilding;
using Gatehouse.Application.LiveBuilding.Dtos;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.LiveBuilding;
using Gatehouse.Domain.Sites;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wire = Gatehouse.Connector.Contracts.Events;

namespace Gatehouse.IntegrationTests.LiveBuilding;

public sealed class LiveFloorTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Tenant = "nordlys";
    private const string Instance = "HQ-Copenhagen";

    private readonly GatehouseApiFactory _api = new(postgres);
    private readonly DateTimeOffset _t0 = DateTimeOffset.UtcNow.AddMinutes(-5);
    private FulfillmentMode _mode;

    public async ValueTask InitializeAsync()
    {
        await _api.InitializeAsync();
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        _mode = (await db.PacsInstances.AsNoTracking().SingleAsync(p => p.Name == Instance)).FulfillmentMode;
        await db.PacsInstances.Where(p => p.Name == Instance).ExecuteUpdateAsync(s => s.SetProperty(p => p.FulfillmentMode, FulfillmentMode.Connector));
        await ClearAsync(db);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    [Fact]
    public async Task Returns_doors_with_states_and_open_alarms()
    {
        var ct = TestContext.Current.CancellationToken;
        var (floorId, doorId) = await FloorAndDoorAsync("CPH-VAULT");
        var forced = new NormalizedDoorEvent(Guid.CreateVersion7(), _t0, Wire.DoorEventType.DoorForced, "D-CPH-VAULT", null, null, null, Wire.ReaderDirection.In);
        await using (var scope = await _api.CreateTenantScopeAsync(Tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var instance = await db.PacsInstances.AsNoTracking().SingleAsync(p => p.Name == Instance, ct);
            scope.ServiceProvider.GetRequiredService<IOutboxWriter>().Enqueue(
                new DoorEventsReceived(Tenant, instance.Id, new ConnectorId(Guid.CreateVersion7()), DateTimeOffset.UtcNow, [forced]),
                CommandPriority.Normal);
            await db.SaveChangesAsync(ct);
        }

        await _api.DrainMessagesAsync(Tenant);

        using var jonas = _api.CreateClientFor(DemoUser.Jonas);
        var floor = await jonas.GetFromJsonAsync<LiveFloorDto>(new Uri($"/api/v1/live/floors/{floorId}", UriKind.Relative), ApiJson.Options, ct);

        Assert.NotNull(floor);
        Assert.Equal(floorId, floor.FloorId);
        Assert.NotEmpty(floor.Areas);
        var door = Assert.Single(floor.Doors, d => d.DoorId == doorId.Value);
        Assert.True(door.ConnectorManaged);
        Assert.Equal(DoorState.ForcedOpen, door.State);
        Assert.NotEmpty(door.Readers);
        var alarm = Assert.Single(floor.OpenAlarms);
        Assert.Equal(doorId.Value, alarm.DoorId);
        Assert.Equal(AlarmKind.Forced, alarm.Kind);
        Assert.Equal(forced.EventId, Assert.Single(floor.RecentEvents).EventId);

        using var freja = _api.CreateClientFor(DemoUser.Freja);
        using var admin = await freja.GetAsync(new Uri($"/api/v1/live/floors/{floorId}", UriKind.Relative), ct);
        Assert.Equal(HttpStatusCode.OK, admin.StatusCode);
        using var missing = await jonas.GetAsync(new Uri($"/api/v1/live/floors/{Guid.CreateVersion7()}", UriKind.Relative), ct);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Rebuilds_state_from_events_on_cache_miss()
    {
        var ct = TestContext.Current.CancellationToken;
        var (floorId, doorId) = await FloorAndDoorAsync("CPH-LAB");
        await using (var scope = await _api.CreateTenantScopeAsync(Tenant))
        {
            var cached = await scope.ServiceProvider.GetRequiredService<IDoorStateStore>().GetManyAsync([doorId], ct);
            Assert.Empty(cached);
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var instance = await db.PacsInstances.AsNoTracking().SingleAsync(p => p.Name == Instance, ct);
            db.DoorEvents.Add(DoorEvent.Create(Guid.CreateVersion7(), _t0, _t0, instance.Id, doorId, null, "D-CPH-LAB", null, DoorEventType.AccessGranted, null, null, null));
            db.DoorEvents.Add(DoorEvent.Create(Guid.CreateVersion7(), _t0.AddSeconds(2), _t0, instance.Id, doorId, null, "D-CPH-LAB", null, DoorEventType.DoorOpened, null, null, null));
            await db.SaveChangesAsync(ct);
        }

        using var jonas = _api.CreateClientFor(DemoUser.Jonas);
        var floor = await jonas.GetFromJsonAsync<LiveFloorDto>(new Uri($"/api/v1/live/floors/{floorId}", UriKind.Relative), ApiJson.Options, ct);

        var door = Assert.Single(floor!.Doors, d => d.DoorId == doorId.Value);
        Assert.Equal(DoorState.Open, door.State);
        Assert.Equal(_t0.AddSeconds(2), door.StateSince!.Value, TimeSpan.FromMilliseconds(1));
        Assert.Equal(2, floor.RecentEvents.Count);
        Assert.Equal(DoorEventType.DoorOpened, floor.RecentEvents[0].Type);
    }

    [Fact]
    public async Task Requester_gets_403()
    {
        var ct = TestContext.Current.CancellationToken;
        var (floorId, _) = await FloorAndDoorAsync("CPH-VAULT");
        using var anna = _api.CreateClientFor(DemoUser.Anna);

        using var response = await anna.GetAsync(new Uri($"/api/v1/live/floors/{floorId}", UriKind.Relative), ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task ClearAsync(TenantDbContext db)
    {
        await db.Alarms.ExecuteDeleteAsync(CancellationToken.None);
        await db.DoorEvents.ExecuteDeleteAsync(CancellationToken.None);
    }

    private async Task<(Guid FloorId, DoorId DoorId)> FloorAndDoorAsync(string areaCode)
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var ct = TestContext.Current.CancellationToken;
        var area = await db.Areas.AsNoTracking().SingleAsync(a => a.Code == areaCode, ct);
        var door = await db.Doors.AsNoTracking().Where(d => d.AreaId == area.Id && d.PacsDoorRef == "D-" + areaCode).Select(d => d.Id).SingleAsync(ct);
        return (area.FloorId.Value, door);
    }
}
