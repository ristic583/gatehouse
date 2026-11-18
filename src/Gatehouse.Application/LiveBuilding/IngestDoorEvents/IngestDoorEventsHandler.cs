using Gatehouse.Application.Audit;
using Gatehouse.Application.Common.Diagnostics;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Connectors.Messages;
using Gatehouse.Application.LiveBuilding.Dtos;
using Gatehouse.Application.LiveBuilding.LiveDrift;
using Gatehouse.Application.Tasks;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.LiveBuilding;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;
using Gatehouse.Domain.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Gatehouse.Application.LiveBuilding.IngestDoorEvents;

/// <summary>
/// Stores door events, derives door states and alarms (domain-model.md §2.6) and raises live drift alarms with a
/// <c>DriftAlert</c> task (§2.7). <see cref="HandleAsync"/> only adds to the unit of work (the consumer saves once);
/// <see cref="PublishAsync"/> runs after commit.
/// </summary>
public sealed partial class IngestDoorEventsHandler(
    ITenantDb db,
    IDoorStateStore states,
    IOccupancyStore occupancy,
    DoorStateReader stateReader,
    ILiveUpdatePublisher publisher,
    LiveDriftDetector drift,
    StaffTaskService tasks,
    IAuditLog audit,
    TimeProvider time,
    ILogger<IngestDoorEventsHandler> logger)
{
    private static readonly AuditActor DriftActor = AuditActor.ForSystem("live-drift");

    public async Task<Result<IngestDoorEventsOutcome>> HandleAsync(IngestDoorEventsCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var incoming = command.Events.DistinctBy(e => e.EventId).ToList();
        if (incoming.Count == 0)
        {
            return IngestDoorEventsOutcome.Empty;
        }

        var instance = await db.PacsInstances.FirstOrDefaultAsync(p => p.Id == command.PacsInstanceId, ct);
        if (instance is null)
        {
            logger.LogWarning("Door events for unknown PACS instance {PacsInstanceId} dropped", command.PacsInstanceId.Value);
            return IngestDoorEventsOutcome.Empty;
        }

        var fresh = await WithoutStoredAsync(incoming, ct);
        if (fresh.Count == 0)
        {
            return IngestDoorEventsOutcome.Empty;
        }

        var doors = await DoorsAsync(instance.SiteIds.ToList(), fresh.Select(e => e.PacsDoorRef).Distinct().ToList(), ct);
        var doorIds = doors.Values.Select(d => d.DoorId).ToList();
        var readers = await db.Readers.AsNoTracking().Where(r => doorIds.Contains(r.DoorId))
            .OrderBy(r => r.Id).Select(r => new ReaderInfo(r.Id, r.DoorId, r.Name, r.PacsReaderRef)).ToListAsync(ct);
        var persons = await PersonsAsync(fresh, ct);
        var current = await stateReader.GetAsync(doorIds, ct);
        var openAlarms = (await db.Alarms.AsNoTracking().Where(a => doorIds.Contains(a.DoorId) && a.AcknowledgedAt == null)
            .Select(a => new { a.DoorId, a.Kind }).ToListAsync(ct)).Select(a => (a.DoorId, a.Kind)).ToHashSet();

        var unauthorized = await UnauthorizedGrantsAsync(fresh, doors, persons, instance.Id, ct);

        var siteIdList = instance.SiteIds.ToList();
        var siteZones = await db.Sites.AsNoTracking()
            .Where(s => siteIdList.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.TimeZoneId, ct);
        var now = time.GetUtcNow();
        var updates = new List<LiveUpdateDto>();
        var occupancyChanges = new List<OccupancyChange>();
        var touched = new HashSet<DoorId>();
        foreach (var e in fresh.OrderBy(e => e.OccurredAt))
        {
            var type = Enum.Parse<DoorEventType>(e.Type.ToString());
            var door = doors.GetValueOrDefault(e.PacsDoorRef);
            if (door is null)
            {
                logger.LogWarning("Door event {EventId} references unknown door {PacsDoorRef}", e.EventId, e.PacsDoorRef);
            }

            var reader = door is null
                ? null
                : readers.FirstOrDefault(r => r.DoorId == door.DoorId && e.PacsReaderRef is not null && r.PacsReaderRef == e.PacsReaderRef)
                ?? readers.FirstOrDefault(r => r.DoorId == door.DoorId);
            var personId = Guid.TryParse(e.PersonRef, out var pid) && persons.ContainsKey(new PersonId(pid))
                ? new PersonId(pid)
                : (PersonId?)null;
            var direction = e.Direction is { } d ? Enum.Parse<ReaderDirection>(d.ToString()) : (ReaderDirection?)null;
            GatehouseTelemetry.DoorEventsIngested.Add(1);
            db.DoorEvents.Add(DoorEvent.Create(e.EventId, e.OccurredAt, command.ReceivedAt, instance.Id, door?.DoorId, reader?.ReaderId,
                e.PacsDoorRef, e.PacsReaderRef, type, personId, e.CardNumberHash, direction));
            if (door is null)
            {
                continue;
            }

            if (type == DoorEventType.AccessGranted && direction is { } dir)
            {
                // Applied after commit, once per event id (GH-LIVE-05).
                var localDate = LocalDate(e.OccurredAt, siteZones.GetValueOrDefault(door.SiteId));
                occupancyChanges.Add(new OccupancyChange(e.EventId, door.SiteId, door.AreaId, localDate,
                    OccupancyChange.PresenceKeyOf(personId, e.CardNumberHash), dir == ReaderDirection.In ? 1 : -1));
            }

            updates.Add(new LiveUpdateDto(LiveUpdateKind.DoorEvent, door.SiteId.Value, DoorEvent: new LiveDoorEventDto(
                e.EventId, e.OccurredAt, type, door.DoorId.Value, door.Name, door.AreaId.Value, door.AreaName, reader?.Name,
                personId?.Value, personId is { } p ? persons[p] : null, direction)));

            var before = current.TryGetValue(door.DoorId, out var known)
                ? known
                : new DoorStateDto(door.DoorId.Value, DoorState.Locked, e.OccurredAt, e.OccurredAt);
            if (known is null || e.OccurredAt >= known.LastEventAt)
            {
                // An event older than the door's last applied event is history only; it never moves the live state back (GH-LIVE-04).
                var after = DoorStateMachine.Apply(before.State, type);
                current[
                    door.DoorId] = new DoorStateDto(
                        door.DoorId.Value, after, after == before.State ? before.Since : e.OccurredAt, e.OccurredAt);
                touched.Add(door.DoorId);
            }

            if (before.State != DoorState.Lockdown && Alarm.KindFor(type, before.State) is { } kind && openAlarms.Add((door.DoorId, kind)))
            {
                var alarm = Alarm.Raise(door.DoorId, kind, e.EventId, now);
                db.Alarms.Add(alarm);
                updates.Add(new LiveUpdateDto(LiveUpdateKind.Alarm, door.SiteId.Value, Alarm: new AlarmDto(
                    alarm.Id.Value, door.DoorId.Value, door.Name, door.AreaId.Value, door.AreaName, door.SiteId.Value, kind, now, e.EventId,
                    null, null, null)));
            }

            // Lockdown does not suppress live drift: a granted swipe in lockdown is still drift.
            if (unauthorized.Contains(e.EventId) && openAlarms.Add((door.DoorId, AlarmKind.UnauthorizedAccess)))
            {
                var alarm = Alarm.RaiseUnauthorizedAccess(door.DoorId, door.AreaId, personId, e.EventId, now);
                db.Alarms.Add(alarm);
                updates.Add(new LiveUpdateDto(LiveUpdateKind.Alarm, door.SiteId.Value, Alarm: new AlarmDto(
                    alarm.Id.Value, door.DoorId.Value, door.Name, door.AreaId.Value, door.AreaName, door.SiteId.Value,
                    AlarmKind.UnauthorizedAccess, now,
                    e.EventId, null, null, null)));
                await tasks.OpenAsync(StaffTaskKind.DriftAlert, door.SiteId, "Alarm", alarm.Id.Value, DriftActor, now, ct);
                audit.Record(DriftActor, "drift.live_alert_raised", "Alarm", alarm.Id.Value.ToString(), new
                {
                    doorId = door.DoorId.Value,
                    areaId = door.AreaId.Value,
                    doorEventId = e.EventId,
                    personId = personId?.Value,
                });
                LogLiveDrift(door.DoorId.Value, e.EventId);
            }
        }

        var changed = touched.Select(id => current[id]).ToList();
        var siteOf = doors.Values.ToDictionary(d => d.DoorId, d => d.SiteId);
        updates.AddRange(
            changed.Select(s => new LiveUpdateDto(LiveUpdateKind.DoorState, siteOf[new DoorId(s.DoorId)].Value, DoorState: s)));
        return new IngestDoorEventsOutcome(changed, updates) { Occupancy = occupancyChanges };
    }

    /// <summary>
    /// Call after the transaction committed: adjust occupancy once per event, cache door states (a state older than the cached one is
    /// not written), then push live updates for what was applied.
    /// </summary>
    public async Task PublishAsync(IngestDoorEventsOutcome outcome, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        foreach (var change in outcome.Occupancy)
        {
            var count = await occupancy.AdjustAsync(change.EventId, change.AreaId, change.LocalDate, change.PresenceKey, change.Delta, ct);
            await publisher.PublishAsync(
                new LiveUpdateDto(LiveUpdateKind.Occupancy, change.SiteId.Value, Occupancy: new OccupancyDto(change.AreaId.Value, count)),
                ct);
        }

        var written = new HashSet<Guid>();
        foreach (var state in outcome.States)
        {
            if (await states.SetAsync(state, ct))
            {
                written.Add(state.DoorId);
            }
        }

        foreach (var update in outcome.Updates)
        {
            if (update.Kind == LiveUpdateKind.DoorState && update.DoorState is { } doorState && !written.Contains(doorState.DoorId))
            {
                continue;
            }

            await publisher.PublishAsync(update, ct);
        }
    }

    // Only granted events at known doors that identify someone (person ref or card) are checked; anonymous grants
    // (exit buttons, remote unlocks) cannot be attributed and are not drift.
    private async Task<IReadOnlySet<Guid>> UnauthorizedGrantsAsync(
        List<NormalizedDoorEvent> events,
        Dictionary<string, DoorInfo> doors,
        Dictionary<PersonId, string> persons,
        PacsInstanceId instanceId,
        CancellationToken ct)
    {
        var grants = events
            .Where(e => e.Type == Connector.Contracts.Events.DoorEventType.AccessGranted
                && (!string.IsNullOrEmpty(e.PersonRef) || !string.IsNullOrEmpty(e.CardNumberHash))
                && doors.ContainsKey(e.PacsDoorRef))
            .Select(e => (
                e.EventId,
                Guid.TryParse(e.PersonRef, out var pid) && persons.ContainsKey(new PersonId(pid)) ? new PersonId(pid) : (PersonId?)null,
                doors[e.PacsDoorRef].AreaId,
                e.OccurredAt))
            .ToList();
        return await drift.FindUnauthorizedAsync(grants, instanceId, ct);
    }

    private async Task<List<NormalizedDoorEvent>> WithoutStoredAsync(List<NormalizedDoorEvent> events, CancellationToken ct)
    {
        var ids = events.Select(e => e.EventId).ToList();
        var from = events.Min(e => e.OccurredAt);
        var to = events.Max(e => e.OccurredAt);
        var stored = (await db.DoorEvents.AsNoTracking()
            .Where(e => e.OccurredAt >= from && e.OccurredAt <= to && ids.Contains(e.Id))
            .Select(e => e.Id).ToListAsync(ct)).ToHashSet();
        return events.Where(e => !stored.Contains(e.EventId)).ToList();
    }

    private async Task<Dictionary<string, DoorInfo>> DoorsAsync(List<SiteId> siteIds, List<string> refs, CancellationToken ct)
    {
        var doors = await (
            from d in db.Doors.AsNoTracking()
            join a in db.Areas.AsNoTracking() on d.AreaId equals a.Id
            join f in db.Floors.AsNoTracking() on a.FloorId equals f.Id
            where d.PacsDoorRef != null && refs.Contains(d.PacsDoorRef) && siteIds.Contains(f.SiteId)
            select new DoorInfo(d.Id, d.Name, d.PacsDoorRef!, a.Id, a.Name, f.SiteId)).ToListAsync(ct);
        return doors.GroupBy(d => d.PacsDoorRef, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
    }

    private async Task<Dictionary<PersonId, string>> PersonsAsync(List<NormalizedDoorEvent> events, CancellationToken ct)
    {
        var ids = events.Select(e => Guid.TryParse(e.PersonRef, out var g) ? new PersonId(g) : (PersonId?)null)
            .OfType<PersonId>().Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        return await db.Persons.AsNoTracking().Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.GivenName + " " + p.FamilyName, ct);
    }

    /// <summary>The site-local calendar date of an event (occupancy resets at local midnight).</summary>
    public static DateOnly LocalDate(DateTimeOffset at, string? timeZoneId)
    {
        var zone = timeZoneId is not null && TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var tz) ? tz : TimeZoneInfo.Utc;
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone).DateTime);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Live drift alarm raised at door {DoorId} for event {EventId}")]
    private partial void LogLiveDrift(Guid doorId, Guid eventId);

    private sealed record DoorInfo(DoorId DoorId, string Name, string PacsDoorRef, AreaId AreaId, string AreaName, SiteId SiteId);

    private sealed record ReaderInfo(ReaderId ReaderId, DoorId DoorId, string Name, string? PacsReaderRef);
}
