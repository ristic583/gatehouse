using Gatehouse.Application.LiveBuilding.Dtos;
using Gatehouse.Application.LiveBuilding.GetLiveFloor;
using Gatehouse.Application.People.Dtos;
using Gatehouse.Connector.Contracts.Events;
using Gatehouse.IntegrationTests.Scenarios.Infrastructure;

namespace Gatehouse.IntegrationTests.Scenarios;

/// <summary>Scenarios 14, 15, 19 (improvements.md §A2, conventions.md §4.8, §4.9, §6): live state never moves back, effects once, feeds re-checked.</summary>
[Trait("Category", "AppHost")]
[Collection(ScenarioCollection.Name)]
public sealed class LiveEventScenarios(ScenarioAppHostFixture fixture)
{
    /// <summary>LiveSubscriptionRevalidator.Interval (60 s) plus margin.</summary>
    private static readonly TimeSpan RevalidationLimit = TimeSpan.FromSeconds(75);
    private static readonly TimeSpan IngestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Anna Larsen (employee) is made security staff for scenario 19 only; the role is removed in the test and again in finally.</summary>
    private const string TemporaryStaff = "anna.larsen@nordlys.test";

    [Fact]
    public async Task Delayed_door_event_never_moves_live_state_backwards()
    {
        using var cts = ScenarioAppHostFixture.NewTestTimeout();
        var ct = cts.Token;
        var staff = await fixture.ClientAsync(ScenarioPersonas.SecurityStaff, ct);
        var (floorId, door) = await QuietHqDoorAsync(staff, ct);
        var t = DateTimeOffset.UtcNow;
        var relocked = new DoorEvent(Guid.CreateVersion7(), t, DoorEventType.DoorRelocked, door.PacsDoorRef!, null, null, null, null);
        var lateUnlock = new DoorEvent(Guid.CreateVersion7(), t.AddSeconds(-30), DoorEventType.DoorUnlocked, door.PacsDoorRef!, null, null, null, null);

        await fixture.Gateway.PostDoorEventsAsync([relocked], ct);
        await WaitForEventAsync(staff, floorId, relocked.EventId, t, ct);
        await fixture.Gateway.PostDoorEventsAsync([lateUnlock], ct);
        await WaitForEventAsync(staff, floorId, lateUnlock.EventId, t, ct);

        var live = await staff.GetAsync<LiveFloorDto>($"v1/live/floors/{floorId}", ct);
        Assert.Equal(Gatehouse.Domain.LiveBuilding.DoorState.Locked, live.Doors.Single(d => d.DoorId == door.DoorId).State);
    }

    [Fact]
    public async Task Duplicate_event_delivery_counts_occupancy_and_alarms_once()
    {
        using var cts = ScenarioAppHostFixture.NewTestTimeout();
        var ct = cts.Token;
        var admin = await fixture.ClientAsync(ScenarioPersonas.TenantAdmin, ct);
        var staff = await fixture.ClientAsync(ScenarioPersonas.SecurityStaff, ct);
        var person = await ScenarioPeople.NewEmployeeWithCardAsync(admin, await fixture.ClientAsync(ScenarioPersonas.Manager, ct), ct);
        var (floorId, door) = await QuietHqDoorAsync(staff, ct);
        // An open alarm left on this door by an earlier run would absorb the new one (one open alarm per door and kind).
        foreach (var open in (await staff.GetAsync<List<AlarmDto>>("v1/live/alarms", ct)).Where(a => a.DoorId == door.DoorId))
        {
            using var acknowledged = await staff.PostAsync(
                $"v1/live/alarms/{open.AlarmId}/acknowledge", new { comment = "Scenario 14 baseline" }, ct);
            acknowledged.EnsureSuccessStatusCode();
        }

        var before = Occupancy(await staff.GetAsync<LiveFloorDto>($"v1/live/floors/{floorId}", ct), door.AreaId);
        var t = DateTimeOffset.UtcNow;
        DoorEvent[] batch =
        [
            new(Guid.CreateVersion7(), t, DoorEventType.AccessGranted, door.PacsDoorRef!, null, person.CardNumber, person.PersonId.ToString(), ReaderDirection.In),
            new(Guid.CreateVersion7(), t.AddMilliseconds(10), DoorEventType.DoorForced, door.PacsDoorRef!, null, null, null, null),
        ];

        var first = await fixture.Gateway.PostDoorEventsAsync(batch, ct);
        var second = await fixture.Gateway.PostDoorEventsAsync(batch, ct);
        Assert.Equal(2, first.Accepted + first.Duplicates);
        await WaitForEventAsync(staff, floorId, batch[1].EventId, t, ct);
        await Task.Delay(TimeSpan.FromSeconds(3), ct);

        var after = Occupancy(await staff.GetAsync<LiveFloorDto>($"v1/live/floors/{floorId}", ct), door.AreaId);
        Assert.True(second.Duplicates == 2 || second.Accepted + second.Duplicates == 2, $"second delivery: {second}");
        Assert.Equal(before + 1, after);
        var alarms = await staff.GetAsync<List<AlarmDto>>("v1/live/alarms", ct);
        Assert.Single(alarms, a => a.DoorEventId == batch[1].EventId);
    }

    [Fact]
    public async Task Role_removed_while_connected_to_live_stops_feed_within_revalidation_interval()
    {
        using var cts = ScenarioAppHostFixture.NewTestTimeout();
        var ct = cts.Token;
        var admin = await fixture.ClientAsync(ScenarioPersonas.TenantAdmin, ct);
        var staff = await fixture.ClientAsync(ScenarioPersonas.SecurityStaff, ct);
        var (floorId, door) = await QuietHqDoorAsync(staff, ct);
        var siteId = (await staff.GetAsync<LiveFloorDto>($"v1/live/floors/{floorId}", ct)).SiteId;
        var annaId = await ScenarioPeople.PersonIdAsync(admin, TemporaryStaff, ct);
        var assignment = await admin.PostAsync<RoleAssignmentDto>(
            "v1/admin/role-assignments", new { personId = annaId, role = "SecurityStaff", siteId }, ct);
        try
        {
            var anna = await fixture.ClientAsync(TemporaryStaff, ct);
            await using var annaFeed = await LiveHubProbe.JoinSiteAsync(anna, siteId, ct);
            await using var jonasFeed = await LiveHubProbe.JoinSiteAsync(staff, siteId, ct);
            await PulseAsync(door, ct);
            var delivered = await PacsProbe.WaitUntilAsync(_ => Task.FromResult(annaFeed.Received.Count > 0 && jonasFeed.Received.Count > 0), IngestTimeout, ct);
            Assert.True(delivered, "Live updates did not reach both feeds before the role removal.");

            await RemoveRoleAsync(admin, assignment.Id, ct);
            var removedAt = DateTimeOffset.UtcNow;
            while (DateTimeOffset.UtcNow < removedAt + RevalidationLimit + TimeSpan.FromSeconds(15))
            {
                await PulseAsync(door, ct);
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }

            var cutoff = removedAt + RevalidationLimit;
            Assert.DoesNotContain(annaFeed.Received, at => at > cutoff);
            Assert.Contains(jonasFeed.Received, at => at > cutoff);
        }
        finally
        {
            var left = await admin.GetAsync<List<RoleAssignmentDto>>("v1/admin/role-assignments", CancellationToken.None);
            foreach (var stale in left.Where(r => r.Id == assignment.Id))
            {
                await RemoveRoleAsync(admin, stale.Id, CancellationToken.None);
            }
        }
    }

    private static long Occupancy(LiveFloorDto floor, Guid areaId) => floor.Occupancy?.FirstOrDefault(o => o.AreaId == areaId)?.Count ?? 0;

    private static async Task RemoveRoleAsync(ScenarioClient admin, Guid assignmentId, CancellationToken ct)
    {
        using var response = await admin.DeleteAsync($"v1/admin/role-assignments/{assignmentId}", ct);
        Assert.True(response.IsSuccessStatusCode, $"remove role: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(ct)}");
    }

    /// <summary>One harmless door event (opened, then closed) so the site publishes a live update.</summary>
    private Task<Gatehouse.Connector.Contracts.Protocol.DoorEventsResponse> PulseAsync(LiveDoorDto door, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        return fixture.Gateway.PostDoorEventsAsync(
            [
                new DoorEvent(Guid.CreateVersion7(), now, DoorEventType.DoorOpened, door.PacsDoorRef!, null, null, null, null),
                new DoorEvent(Guid.CreateVersion7(), now.AddMilliseconds(5), DoorEventType.DoorClosed, door.PacsDoorRef!, null, null, null, null),
            ],
            ct);
    }

    /// <summary>
    /// A LegacyPACS door on the HQ floor whose level nobody holds, so the Building.Simulator's background traffic (which swipes doors of
    /// active assignments) does not produce events there during the scenario.
    /// </summary>
    private async Task<(Guid FloorId, LiveDoorDto Door)> QuietHqDoorAsync(ScenarioClient staff, CancellationToken ct)
    {
        var floorId = await fixture.Access.FloorIdAsync(ScenarioAreas.HqLab, ct);
        var floor = await staff.GetAsync<LiveFloorDto>($"v1/live/floors/{floorId}", ct);
        var legacyDoors = (await fixture.Pacs.LegacyDoorsAsync(ct)).ToDictionary(d => d.DoorRef, StringComparer.Ordinal);
        var assignedLevels = (await fixture.Pacs.AllLegacyAssignmentsAsync(ct)).Where(a => a.IsActive).Select(a => a.AccessLevelRef).ToHashSet(StringComparer.Ordinal);
        var door = floor.Doors.FirstOrDefault(d => d.ConnectorManaged && d.PacsDoorRef is { } r && legacyDoors.TryGetValue(r, out var l) && !assignedLevels.Contains(l.AccessLevelRef) && !l.Lockdown)
            ?? throw new InvalidOperationException("No HQ door without active assignments on its level.");
        return (floorId, door);
    }

    private static async Task WaitForEventAsync(ScenarioClient staff, Guid floorId, Guid eventId, DateTimeOffset around, CancellationToken ct)
    {
        var from = Uri.EscapeDataString(around.AddMinutes(-5).ToString("O"));
        var to = Uri.EscapeDataString(around.AddMinutes(5).ToString("O"));
        var id = eventId.ToString();
        var seen = await PacsProbe.WaitUntilAsync(
            async c =>
            {
                using var response = await staff.SendAsync(HttpMethod.Get, $"v1/live/floors/{floorId}/events?from={from}&to={to}", null, c);
                return response.IsSuccessStatusCode && (await response.Content.ReadAsStringAsync(c)).Contains(id, StringComparison.OrdinalIgnoreCase);
            },
            IngestTimeout,
            ct);
        Assert.True(seen, $"Door event {eventId} is not in the floor history.");
    }
}
