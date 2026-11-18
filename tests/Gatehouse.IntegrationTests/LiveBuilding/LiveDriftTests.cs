using Gatehouse.Application.Common.Messaging;
using Gatehouse.Application.Connectors.Messages;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.LiveBuilding;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;
using Gatehouse.Domain.Tasks;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Gatehouse.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Wire = Gatehouse.Connector.Contracts.Events;

namespace Gatehouse.IntegrationTests.LiveBuilding;

/// <summary>Live drift at ingest (domain-model.md §2.7) on HQ-Copenhagen's vault door; all test rows are removed.</summary>
public sealed class LiveDriftTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Tenant = "nordlys";
    private const string DoorRef = "D-CPH-VAULT";

    private readonly GatehouseApiFactory _api = new(postgres);
    private readonly List<Guid> _events = [];
    private readonly List<PersonId> _people = [];
    private readonly List<EntitlementId> _entitlements = [];
    private PacsInstanceId _instanceId;
    private AreaId _areaId;
    private DoorId _doorId;

    public async ValueTask InitializeAsync()
    {
        await _api.InitializeAsync();
        await using (var scope = await _api.CreateTenantScopeAsync(Tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            _instanceId = (await db.PacsInstances.AsNoTracking().SingleAsync(p => p.Name == "HQ-Copenhagen")).Id;
            _areaId = (await db.Areas.AsNoTracking().SingleAsync(a => a.Code == "CPH-VAULT")).Id;
            _doorId = (await db.Doors.AsNoTracking().SingleAsync(d => d.PacsDoorRef == DoorRef)).Id;
        }

        await ClearAlarmsAsync();
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    [Fact]
    public async Task Granted_without_entitlement_raises_unauthorized_alarm_and_task()
    {
        var ct = TestContext.Current.CancellationToken;
        var person = await PersonAsync(ct);
        var ev = Event(Wire.DoorEventType.AccessGranted, person.Value.ToString());

        await IngestAsync(ev);

        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var alarm = Assert.Single(await db.Alarms.AsNoTracking().Where(a => a.DoorId == _doorId).ToListAsync(ct));
        Assert.Equal(AlarmKind.UnauthorizedAccess, alarm.Kind);
        Assert.Equal(ev.EventId, alarm.DoorEventId);
        var task = await db.StaffTasks.AsNoTracking().SingleAsync(t => t.SubjectId == alarm.Id.Value, ct);
        Assert.Equal(StaffTaskKind.DriftAlert, task.Kind);
        Assert.Equal("Alarm", task.SubjectType);
        Assert.Equal(StaffTaskStatus.Open, task.Status);
        Assert.True(await db.AuditEntries.AnyAsync(a => a.Action == "drift.live_alert_raised" && a.SubjectId == alarm.Id.Value.ToString(), ct));
    }

    [Fact]
    public async Task Granted_with_confirmed_entitlement_raises_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var person = await PersonAsync(ct);
        await EntitlementAsync(person, FulfillmentStatus.Confirmed, ct);

        await IngestAsync(Event(Wire.DoorEventType.AccessGranted, person.Value.ToString()));

        Assert.Empty(await AlarmsAsync(ct));
    }

    [Fact]
    public async Task Granted_while_grant_pending_raises_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var person = await PersonAsync(ct);
        await EntitlementAsync(person, FulfillmentStatus.Pending, ct);

        await IngestAsync(Event(Wire.DoorEventType.AccessGranted, person.Value.ToString()));

        Assert.Empty(await AlarmsAsync(ct));
    }

    [Fact]
    public async Task Granted_for_unknown_person_raises_alarm()
    {
        var ct = TestContext.Current.CancellationToken;

        await IngestAsync(Event(Wire.DoorEventType.AccessGranted, Guid.NewGuid().ToString()));

        Assert.Equal(AlarmKind.UnauthorizedAccess, Assert.Single(await AlarmsAsync(ct)).Kind);
    }

    [Fact]
    public async Task Open_alarm_is_not_duplicated()
    {
        var ct = TestContext.Current.CancellationToken;
        var person = await PersonAsync(ct);

        await IngestAsync(Event(Wire.DoorEventType.AccessGranted, person.Value.ToString()));
        await IngestAsync(Event(Wire.DoorEventType.AccessGranted, person.Value.ToString(), 5));

        var alarm = Assert.Single(await AlarmsAsync(ct));
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        Assert.Equal(1, await db.StaffTasks.CountAsync(t => t.SubjectId == alarm.Id.Value, ct));
    }

    [Fact]
    public async Task Denied_swipe_is_not_drift()
    {
        var ct = TestContext.Current.CancellationToken;
        var person = await PersonAsync(ct);

        await IngestAsync(Event(Wire.DoorEventType.AccessDenied, person.Value.ToString()));

        Assert.Empty(await AlarmsAsync(ct));
    }

    private NormalizedDoorEvent Event(Wire.DoorEventType type, string personRef, int seconds = 0)
    {
        var ev = new NormalizedDoorEvent(Guid.CreateVersion7(), DateTimeOffset.UtcNow.AddMinutes(-1).AddSeconds(seconds), type, DoorRef, null, null, personRef,
            Wire.ReaderDirection.In);
        _events.Add(ev.EventId);
        return ev;
    }

    private async Task IngestAsync(NormalizedDoorEvent ev)
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var scope = await _api.CreateTenantScopeAsync(Tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            scope.ServiceProvider.GetRequiredService<IOutboxWriter>().Enqueue(
                new DoorEventsReceived(Tenant, _instanceId, new ConnectorId(Guid.CreateVersion7()), DateTimeOffset.UtcNow, [ev]),
                CommandPriority.Normal);
            await db.SaveChangesAsync(ct);
        }

        await _api.DrainMessagesAsync(Tenant);
    }

    private async Task<PersonId> PersonAsync(CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var person = MappingData.Employee($"ld-{Guid.NewGuid():N}@x.test");
        db.Add(person);
        await db.SaveChangesAsync(ct);
        _people.Add(person.Id);
        return person.Id;
    }

    private async Task EntitlementAsync(PersonId person, FulfillmentStatus grantStatus, CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var now = DateTimeOffset.UtcNow;
        var entitlement = Entitlement.Create(person, _areaId, person, now.AddDays(-1), now.AddDays(30), now.AddDays(30), "Live drift test",
            [new StepPlan(ApproverKind.AreaOwner, null)], now);
        var grant = Domain.Fulfillment.Fulfillment.Create(entitlement.Id, _instanceId, FulfillmentKind.Grant, CommandPriority.Normal, now);
        entitlement.ClearDomainEvents();
        grant.ClearDomainEvents();
        db.AddRange(entitlement, grant);
        db.Entry(entitlement).Property(e => e.Status).CurrentValue = EntitlementStatus.Approved;
        db.Entry(grant).Property(f => f.Status).CurrentValue = grantStatus;
        await db.SaveChangesAsync(ct);
        _entitlements.Add(entitlement.Id);
    }

    private async Task<List<Alarm>> AlarmsAsync(CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        return await db.Alarms.AsNoTracking().Where(a => a.DoorId == _doorId).ToListAsync(ct);
    }

    private async Task ClearAlarmsAsync()
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var door = _doorId;
        var alarmIds = await db.Alarms.Where(a => a.DoorId == door).Select(a => a.Id.Value).ToListAsync(CancellationToken.None);
        await db.StaffTasks.Where(t => t.Kind == StaffTaskKind.DriftAlert && alarmIds.Contains(t.SubjectId)).ExecuteDeleteAsync(CancellationToken.None);
        await db.Alarms.Where(a => a.DoorId == door).ExecuteDeleteAsync(CancellationToken.None);
    }
}
