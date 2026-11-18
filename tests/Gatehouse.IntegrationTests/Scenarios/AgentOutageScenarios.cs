using Gatehouse.Application.LiveBuilding.Dtos;
using Gatehouse.Application.LiveBuilding.RequestDoorCommand;
using Gatehouse.IntegrationTests.Scenarios.Infrastructure;

namespace Gatehouse.IntegrationTests.Scenarios;

/// <summary>Scenarios 7, 8, 11 (improvements.md §A2, conventions.md §4.5, §4.6): superseded is final, deadlines hold, lockdown is fast.</summary>
[Trait("Category", "AppHost")]
[Collection(ScenarioCollection.Name)]
public sealed class AgentOutageScenarios(ScenarioAppHostFixture fixture)
{
    /// <summary>Lockdown target on a slow PACS (a lab finding).</summary>
    private static readonly TimeSpan LockdownLimit = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(90);

    [Fact]
    public async Task Old_grant_retry_is_never_applied_after_revoke_while_agent_offline()
    {
        using var cts = ScenarioAppHostFixture.NewTestTimeout();
        var ct = cts.Token;
        var area = ScenarioAreas.HqLab;
        var admin = await fixture.ClientAsync(ScenarioPersonas.TenantAdmin, ct);
        var staff = await fixture.ClientAsync(ScenarioPersonas.SecurityStaff, ct);
        var person = await ScenarioPeople.NewEmployeeWithCardAsync(admin, await fixture.ClientAsync(ScenarioPersonas.Manager, ct), ct);
        var now = DateTimeOffset.UtcNow;

        // A grant can be retried only once it failed: LegacyPACS refuses its writes (cardholder upsert and grant) permanently first.
        Guid entitlement;
        await using (await fixture.Pacs.LegacyChaosAsync(failNext: 0, failNextPermanent: 10, delayMs: 0, ct))
        {
            entitlement = await fixture.Access.RequestAndApproveAsync(person, area, now, now.AddDays(7), ct);
            await fixture.Access.WaitForFulfillmentsAsync(entitlement, "Grant", "Failed", ScenarioAccess.ConfirmTimeout, ct);
        }

        var grant = (await fixture.Db.FulfillmentsAsync(entitlement, ct)).Single(f => f.Kind == "Grant");
        await fixture.Agents.StopAsync(AgentControl.HqAgent, ct);
        try
        {
            using (var retry = await staff.PostAsync($"v1/fulfillments/{grant.FulfillmentId}/retry", null, ct))
            {
                Assert.True(retry.IsSuccessStatusCode, $"retry: {(int)retry.StatusCode} {await retry.Content.ReadAsStringAsync(ct)}");
            }

            var owner = await fixture.ClientAsync(ScenarioPersonas.AreaOwner, ct);
            await ScenarioAccess.RevokeAsync(owner, entitlement, ct);
        }
        finally
        {
            await fixture.Agents.StartAsync(AgentControl.HqAgent, CancellationToken.None);
        }

        var drained = await PacsProbe.WaitUntilAsync(
            async c => (await fixture.Db.FulfillmentsAsync(entitlement, c)).All(f => f.Status is not ("Desired" or "Pending")), DrainTimeout, ct);
        Assert.True(drained, "The queue of the entitlement did not drain.");
        Assert.Equal("Superseded", (await fixture.Db.FulfillmentsAsync(entitlement, ct)).Single(f => f.FulfillmentId == grant.FulfillmentId).Status);
        Assert.DoesNotContain(await fixture.Pacs.LegacyAssignmentsAsync(person.CardNumber, ct), a => a.AccessLevelRef == area.AccessLevelRef && a.IsActive);
        ScenarioAsserts.Denied(await fixture.Swipes.SwipeAsync(area.DoorRef, person.CardNumber, ct), "old grant retried before the revoke");
    }

    [Fact]
    public async Task Momentary_unlock_is_not_executed_when_agent_returns_after_deadline()
    {
        using var cts = ScenarioAppHostFixture.NewTestTimeout();
        var ct = cts.Token;
        var staff = await fixture.ClientAsync(ScenarioPersonas.SecurityStaff, ct);
        var door = await HqDoorAsync(staff, ScenarioAreas.HqLab.DoorRef, ct);

        await fixture.Agents.StopAsync(AgentControl.HqAgent, ct);
        var requestedAt = DateTimeOffset.UtcNow;
        DoorCommandResultDto unlock;
        try
        {
            unlock = await staff.PostAsync<DoorCommandResultDto>(
                "v1/live/door-commands", new { kind = "MomentaryUnlock", scope = "Door", scopeId = door.DoorId, reason = "M11 scenario 8", durationSeconds = 5 }, ct);
            await Task.Delay(requestedAt.AddSeconds(65) - DateTimeOffset.UtcNow, ct);
        }
        finally
        {
            await fixture.Agents.StartAsync(AgentControl.HqAgent, CancellationToken.None);
        }

        IReadOnlyList<(string Status, string? LastError)> commands = [];
        var ended = await PacsProbe.WaitUntilAsync(
            async c =>
            {
                commands = await fixture.Db.DoorConnectorCommandsAsync(unlock.DoorCommandId, c);
                return commands.Count > 0 && commands.All(x => x.Status != "Queued" && x.Status != "Delivered");
            },
            DrainTimeout,
            ct);
        Assert.True(ended, "The unlock command is still queued after the agent returned.");
        Assert.All(commands, x => Assert.Equal(("Failed", "door_command.expired"), (x.Status, x.LastError)));

        // A few more agent polls: the door must never report an unlock that started after the request.
        await Task.Delay(TimeSpan.FromSeconds(10), ct);
        var legacy = (await fixture.Pacs.LegacyDoorsAsync(ct)).Single(d => d.DoorRef == door.PacsDoorRef);
        Assert.False(legacy.UnlockedUntil is { } until && until > requestedAt, $"{door.PacsDoorRef} was unlocked until {legacy.UnlockedUntil}.");
    }

    [Fact]
    public async Task Lockdown_on_slow_pacs_locks_all_floor_doors_within_limit()
    {
        using var cts = ScenarioAppHostFixture.NewTestTimeout();
        var ct = cts.Token;
        var staff = await fixture.ClientAsync(ScenarioPersonas.SecurityStaff, ct);
        var floorId = await fixture.Access.FloorIdAsync(ScenarioAreas.HqLab, ct);
        var floor = await staff.GetAsync<LiveFloorDto>($"v1/live/floors/{floorId}", ct);
        var doorRefs = floor.Doors.Where(d => d.ConnectorManaged && d.PacsDoorRef is not null).Select(d => d.PacsDoorRef!).ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(doorRefs);

        await using (await fixture.Swipes.ScenarioAsync("pacs-slow", ct))
        {
            try
            {
                var started = DateTimeOffset.UtcNow;
                await staff.PostAsync<DoorCommandResultDto>(
                    "v1/live/door-commands", new { kind = "LockdownStart", scope = "Floor", scopeId = floorId, reason = "M11 scenario 11" }, ct);
                var locked = await PacsProbe.WaitUntilAsync(
                    async c => (await fixture.Pacs.LegacyDoorsAsync(c)).Where(d => doorRefs.Contains(d.DoorRef)).Count(d => d.Lockdown) == doorRefs.Count,
                    LockdownLimit,
                    ct);
                Assert.True(locked, $"Not every door of the floor reported lockdown within {LockdownLimit} (started {started:O}).");
            }
            finally
            {
                await staff.PostAsync<DoorCommandResultDto>(
                    "v1/live/door-commands", new { kind = "LockdownClear", scope = "Floor", scopeId = floorId, reason = "M11 scenario 11 cleared" }, CancellationToken.None);
            }
        }

        var cleared = await PacsProbe.WaitUntilAsync(
            async c => !(await fixture.Pacs.LegacyDoorsAsync(c)).Any(d => doorRefs.Contains(d.DoorRef) && d.Lockdown), DrainTimeout, ct);
        Assert.True(cleared, "Lockdown was not cleared on the floor.");
    }

    private async Task<Application.LiveBuilding.GetLiveFloor.LiveDoorDto> HqDoorAsync(ScenarioClient staff, string doorRef, CancellationToken ct)
    {
        var floorId = await fixture.Access.FloorIdAsync(ScenarioAreas.HqLab, ct);
        var floor = await staff.GetAsync<LiveFloorDto>($"v1/live/floors/{floorId}", ct);
        return floor.Doors.Single(d => d.PacsDoorRef == doorRef);
    }
}
