using Gatehouse.Application.Common.Messaging;
using Gatehouse.Application.Connectors.Messages;
using Gatehouse.Application.LiveBuilding;
using Gatehouse.Application.LiveBuilding.Dtos;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Common;
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

public sealed class DoorEventIngestTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Tenant = "nordlys";

    private readonly GatehouseApiFactory _api = new(postgres);
    private readonly DateTimeOffset _t0 = DateTimeOffset.UtcNow.AddMinutes(-5);

    public async ValueTask InitializeAsync()
    {
        await _api.InitializeAsync();
        await ClearAsync();
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    [Fact]
    public async Task Granted_swipe_stores_event_and_unlocks_door()
    {
        var ct = TestContext.Current.CancellationToken;
        var anna = await AnnaIdAsync();
        var ev = Event(0, Wire.DoorEventType.AccessGranted, "D-CPH-SERVER", personRef: anna.ToString());

        await IngestAsync(ev);

        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var stored = await db.DoorEvents.AsNoTracking().SingleAsync(e => e.Id == ev.EventId, ct);
        var door = await DoorAsync("CPH-SERVER");
        Assert.Equal(door, stored.DoorId);
        Assert.NotNull(stored.ReaderId);
        Assert.Equal(anna, stored.PersonId!.Value.Value);
        Assert.Equal(DoorEventType.AccessGranted, stored.Type);
        Assert.Equal(DoorState.Unlocked, (await StateAsync(door))!.State);
    }

    [Fact]
    public async Task Delayed_older_event_does_not_move_state_back()
    {
        var ct = TestContext.Current.CancellationToken;
        var door = await DoorAsync("CPH-SERVER");
        await IngestAsync(Event(0, Wire.DoorEventType.DoorUnlocked, "D-CPH-SERVER"), Event(10, Wire.DoorEventType.DoorRelocked, "D-CPH-SERVER"));
        var late = Event(5, Wire.DoorEventType.DoorUnlocked, "D-CPH-SERVER");

        await IngestAsync(late);

        var state = (await StateAsync(door))!;
        Assert.Equal(DoorState.Locked, state.State);
        Assert.Equal(_t0.AddSeconds(10), state.LastEventAt, TimeSpan.FromMilliseconds(1));
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        Assert.True(await scope.ServiceProvider.GetRequiredService<TenantDbContext>().DoorEvents.AsNoTracking().AnyAsync(e => e.Id == late.EventId, ct));
    }

    [Fact]
    public async Task Forced_door_raises_one_alarm()
    {
        await IngestAsync(Event(0, Wire.DoorEventType.DoorForced, "D-CPH-VAULT"), Event(1, Wire.DoorEventType.DoorForced, "D-CPH-VAULT"));

        var door = await DoorAsync("CPH-VAULT");
        var alarm = Assert.Single(await AlarmsAsync(door));
        Assert.Equal(AlarmKind.Forced, alarm.Kind);
        Assert.Null(alarm.AcknowledgedAt);
        Assert.Equal(DoorState.ForcedOpen, (await StateAsync(door))!.State);
    }

    [Fact]
    public async Task Held_open_raises_alarm()
    {
        await IngestAsync(
            Event(0, Wire.DoorEventType.AccessGranted, "D-CPH-LAB"),
            Event(1, Wire.DoorEventType.DoorOpened, "D-CPH-LAB"),
            Event(40, Wire.DoorEventType.DoorHeldOpen, "D-CPH-LAB"));

        var door = await DoorAsync("CPH-LAB");
        Assert.Equal(AlarmKind.HeldOpen, Assert.Single(await AlarmsAsync(door)).Kind);
        Assert.Equal(DoorState.HeldOpen, (await StateAsync(door))!.State);
    }

    [Fact]
    public async Task Lockdown_ignores_events_until_cleared()
    {
        var ct = TestContext.Current.CancellationToken;
        var door = await DoorAsync("CPH-PLANT");
        await IngestAsync(
            Event(0, Wire.DoorEventType.LockdownStarted, "D-CPH-PLANT"),
            Event(1, Wire.DoorEventType.AccessGranted, "D-CPH-PLANT"),
            Event(2, Wire.DoorEventType.DoorForced, "D-CPH-PLANT"));

        Assert.Equal(DoorState.Lockdown, (await StateAsync(door))!.State);
        Assert.Empty(await AlarmsAsync(door));
        await using (var scope = await _api.CreateTenantScopeAsync(Tenant))
        {
            Assert.Equal(3, await scope.ServiceProvider.GetRequiredService<TenantDbContext>().DoorEvents.CountAsync(e => e.DoorId == door, ct));
        }

        await IngestAsync(Event(3, Wire.DoorEventType.LockdownCleared, "D-CPH-PLANT"), Event(4, Wire.DoorEventType.AccessGranted, "D-CPH-PLANT"));

        Assert.Equal(DoorState.Unlocked, (await StateAsync(door))!.State);
    }

    [Fact]
    public async Task Replayed_message_does_not_duplicate()
    {
        var ct = TestContext.Current.CancellationToken;
        var forced = Event(0, Wire.DoorEventType.DoorForced, "D-CPH-LOBBY");
        var closed = Event(1, Wire.DoorEventType.DoorClosed, "D-CPH-LOBBY");

        await IngestAsync(forced, closed);
        await IngestAsync(forced, closed);

        var door = await DoorAsync("CPH-LOBBY");
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<TenantDbContext>().DoorEvents.CountAsync(e => e.DoorId == door, ct));
        Assert.Single(await AlarmsAsync(door));
        Assert.Equal(DoorState.Closed, (await StateAsync(door))!.State);
    }

    [Fact]
    public async Task Live_updates_published_per_site()
    {
        var ct = TestContext.Current.CancellationToken;
        var messaging = await _api.MessagingAsync();
        var received = new List<(string Tenant, LiveUpdateDto Update)>();
        await messaging.Services.GetRequiredService<ILiveUpdateSubscriber>().SubscribeAsync((tenant, update) =>
        {
            lock (received)
            {
                received.Add((tenant, update));
            }

            return Task.CompletedTask;
        }, ct);
        var ev = Event(0, Wire.DoorEventType.DoorForced, "D-CPH-OFFICE-1");

        await IngestAsync(ev);

        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var siteId = (await db.Sites.AsNoTracking().SingleAsync(s => s.Code == "CPH-HQ", ct)).Id.Value;
        var door = await DoorAsync("CPH-OFFICE-1");
        Assert.All(received, r => Assert.Equal(Tenant, r.Tenant));
        Assert.All(received, r => Assert.Equal(siteId, r.Update.SiteId));
        var doorEvent = Assert.Single(received, r => r.Update.Kind == LiveUpdateKind.DoorEvent).Update.DoorEvent!;
        Assert.Equal(ev.EventId, doorEvent.EventId);
        Assert.Equal("Office First", doorEvent.AreaName);
        Assert.Equal(DoorState.ForcedOpen, Assert.Single(received, r => r.Update.Kind == LiveUpdateKind.DoorState).Update.DoorState!.State);
        Assert.Equal(door.Value, Assert.Single(received, r => r.Update.Kind == LiveUpdateKind.Alarm).Update.Alarm!.DoorId);
    }

    private NormalizedDoorEvent Event(int seconds, Wire.DoorEventType type, string doorRef, string? personRef = null) =>
        new(Guid.CreateVersion7(), _t0.AddSeconds(seconds), type, doorRef, null, null, personRef, Wire.ReaderDirection.In);

    private async Task IngestAsync(params NormalizedDoorEvent[] events)
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var scope = await _api.CreateTenantScopeAsync(Tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var instance = await db.PacsInstances.AsNoTracking().SingleAsync(p => p.Name == "HQ-Copenhagen", ct);
            scope.ServiceProvider.GetRequiredService<IOutboxWriter>().Enqueue(
                new DoorEventsReceived(Tenant, instance.Id, new ConnectorId(Guid.CreateVersion7()), DateTimeOffset.UtcNow, events),
                CommandPriority.Normal);
            await db.SaveChangesAsync(ct);
        }

        await _api.DrainMessagesAsync(Tenant);
    }

    private async Task<DoorStateDto?> StateAsync(DoorId door)
    {
        var messaging = await _api.MessagingAsync();
        await using var scope = await messaging.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(new TenantId(Tenant), CancellationToken.None);
        var states = await scope.ServiceProvider.GetRequiredService<IDoorStateStore>().GetManyAsync([door], CancellationToken.None);
        return states.GetValueOrDefault(door);
    }

    private async Task<DoorId> DoorAsync(string areaCode)
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var ct = TestContext.Current.CancellationToken;
        return await db.Doors.AsNoTracking().Where(d => d.PacsDoorRef == "D-" + areaCode).Select(d => d.Id).SingleAsync(ct);
    }

    private async Task<List<Alarm>> AlarmsAsync(DoorId door)
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        return await scope.ServiceProvider.GetRequiredService<TenantDbContext>().Alarms.AsNoTracking().Where(a => a.DoorId == door).ToListAsync(TestContext.Current.CancellationToken);
    }

    private async Task<Guid> AnnaIdAsync()
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        return (await db.Persons.AsNoTracking().SingleAsync(p => p.Email == DemoUser.Anna.Email, TestContext.Current.CancellationToken)).Id.Value;
    }

    private async Task ClearAsync()
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        await db.Alarms.ExecuteDeleteAsync(CancellationToken.None);
        await db.DoorEvents.ExecuteDeleteAsync(CancellationToken.None);
    }
}
