using Gatehouse.IntegrationTests.Scenarios.Infrastructure;

namespace Gatehouse.IntegrationTests.Scenarios;

/// <summary>Scenarios 3-6 (improvements.md §A2, conventions.md §4.1, §4.3, §4.4): half-open windows, renewals, an explicit revoke at every end.</summary>
[Trait("Category", "AppHost")]
[Collection(ScenarioCollection.Name)]
public sealed class AccessWindowScenarios(ScenarioAppHostFixture fixture)
{
    private static readonly TimeSpan RevokeTimeout = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task Early_renewal_keeps_current_access_until_renewal_starts_then_continues()
    {
        using var cts = ScenarioAppHostFixture.NewTestTimeout();
        var ct = cts.Token;
        var admin = await fixture.ClientAsync(ScenarioPersonas.TenantAdmin, ct);
        var person = await ScenarioPeople.NewEmployeeWithCardAsync(admin, await fixture.ClientAsync(ScenarioPersonas.Manager, ct), ct);
        var now = DateTimeOffset.UtcNow;
        var areas = new[] { ScenarioAreas.HqLab, ScenarioAreas.AarhusDock };
        var originals = new Dictionary<ScenarioArea, Guid>();
        foreach (var area in areas)
        {
            originals[area] = await fixture.Access.GrantAsync(person, area, now, now.AddDays(2), ct);
        }

        // The original end comes close (real wait instead of rewriting command rows, M11 decision 4); the lifecycle opens the renewals.
        var originalEnd = DateTimeOffset.UtcNow.AddSeconds(100);
        foreach (var area in areas)
        {
            await fixture.Clock.SetEntitlementWindowAsync(originals[area], now, originalEnd, ct);
        }

        await ScenarioClock.RunLifecycleAsync(admin, ct);
        var renewals = new Dictionary<ScenarioArea, Guid>();
        foreach (var area in areas)
        {
            var areaId = await fixture.Access.AreaIdAsync(area, ct);
            renewals[area] = await fixture.Db.EntitlementAsync(person.PersonId, areaId, "PendingApproval", ct)
                ?? throw new InvalidOperationException($"The lifecycle opened no renewal for {area.Code}.");
            await fixture.Access.ApproveAsync(renewals[area], requesterEmail: null, ct);
        }

        ScenarioAsserts.Granted(await fixture.Swipes.SwipeAsync(ScenarioAreas.HqLab.DoorRef, person.CardNumber, ct), "LegacyPACS right after the renewal approval");
        ScenarioAsserts.Granted(await fixture.Swipes.SwipeAsync(ScenarioAreas.AarhusDock.DoorRef, person.CardNumber, ct), "CloudPACS right after the renewal approval");

        // LegacyPACS holds windows natively: the renewal extends the window, it never replaces it with the later one (§4.3).
        var renewalEnd = await fixture.Db.EffectiveEndAsync(renewals[ScenarioAreas.HqLab], ct);
        await fixture.Access.WaitForFulfillmentsAsync(renewals[ScenarioAreas.HqLab], "Grant", "Confirmed", ScenarioAccess.ConfirmTimeout, ct);
        var legacy = (await fixture.Pacs.LegacyAssignmentsAsync(person.CardNumber, ct))
            .Where(a => a.AccessLevelRef == ScenarioAreas.HqLab.AccessLevelRef && a.IsActive).ToList();
        Assert.NotEmpty(legacy);
        Assert.True(legacy.Min(a => a.ValidFrom ?? DateTimeOffset.MinValue) <= DateTimeOffset.UtcNow, "LegacyPACS window starts in the future.");
        Assert.True(legacy.Max(a => a.ValidTo ?? DateTimeOffset.MaxValue) >= renewalEnd.AddMinutes(-1), "LegacyPACS window does not cover the renewal.");
        Assert.Contains(await fixture.Pacs.CloudAssignmentsAsync(person.CardNumber, ct), a => a.GroupId == ScenarioAreas.AarhusDock.AccessLevelRef && a.Active);

        // The original window ends, the lifecycle expires it: access continues through the renewal.
        await Task.Delay(Max(TimeSpan.Zero, originalEnd - DateTimeOffset.UtcNow + TimeSpan.FromSeconds(2)), ct);
        await ScenarioClock.RunLifecycleAsync(admin, ct);
        foreach (var area in areas)
        {
            await WaitUntilSettledAsync(originals[area], ct);
            await WaitUntilSettledAsync(renewals[area], ct);
        }

        ScenarioAsserts.Granted(await fixture.Swipes.SwipeAsync(ScenarioAreas.HqLab.DoorRef, person.CardNumber, ct), "LegacyPACS after the original end");
        ScenarioAsserts.Granted(await fixture.Swipes.SwipeAsync(ScenarioAreas.AarhusDock.DoorRef, person.CardNumber, ct), "CloudPACS after the original end");
    }

    [Fact]
    public async Task Natural_expiry_revokes_on_both_pacs_and_card_is_denied()
    {
        using var cts = ScenarioAppHostFixture.NewTestTimeout();
        var ct = cts.Token;
        var admin = await fixture.ClientAsync(ScenarioPersonas.TenantAdmin, ct);
        var person = await ScenarioPeople.NewEmployeeWithCardAsync(admin, await fixture.ClientAsync(ScenarioPersonas.Manager, ct), ct);
        var now = DateTimeOffset.UtcNow;
        var lab = await fixture.Access.GrantAsync(person, ScenarioAreas.HqLab, now, now.AddDays(7), ct);
        var dock = await fixture.Access.GrantAsync(person, ScenarioAreas.AarhusDock, now, now.AddDays(7), ct);

        await fixture.Clock.SetEntitlementWindowAsync(lab, now.AddHours(-1), DateTimeOffset.UtcNow.AddSeconds(-1), ct);
        await fixture.Clock.SetEntitlementWindowAsync(dock, now.AddHours(-1), DateTimeOffset.UtcNow.AddSeconds(-1), ct);
        await ScenarioClock.RunLifecycleAsync(admin, ct);

        await AssertNoAssignmentsAsync(person, ct);
        ScenarioAsserts.Denied(await fixture.Swipes.SwipeAsync(ScenarioAreas.HqLab.DoorRef, person.CardNumber, ct), "LegacyPACS after expiry");
        ScenarioAsserts.Denied(await fixture.Swipes.SwipeAsync(ScenarioAreas.AarhusDock.DoorRef, person.CardNumber, ct), "CloudPACS after expiry");
    }

    [Fact]
    public async Task Shortened_eligibility_removes_every_assignment_at_new_end()
    {
        using var cts = ScenarioAppHostFixture.NewTestTimeout();
        var ct = cts.Token;
        var admin = await fixture.ClientAsync(ScenarioPersonas.TenantAdmin, ct);
        var person = await ScenarioPeople.NewEmployeeWithCardAsync(admin, await fixture.ClientAsync(ScenarioPersonas.Manager, ct), ct);
        var now = DateTimeOffset.UtcNow;
        await fixture.Access.GrantAsync(person, ScenarioAreas.HqLab, now, now.AddDays(7), ct);
        await fixture.Access.GrantAsync(person, ScenarioAreas.AarhusDock, now, now.AddDays(7), ct);

        await fixture.Clock.SetEligibilityEndAsync(person.PersonId, DateTimeOffset.UtcNow.AddSeconds(-1), ct);
        await ScenarioClock.RunLifecycleAsync(admin, ct);

        await AssertNoAssignmentsAsync(person, ct);
        ScenarioAsserts.Denied(await fixture.Swipes.SwipeAsync(ScenarioAreas.HqLab.DoorRef, person.CardNumber, ct), "LegacyPACS after the eligibility end");
        ScenarioAsserts.Denied(await fixture.Swipes.SwipeAsync(ScenarioAreas.AarhusDock.DoorRef, person.CardNumber, ct), "CloudPACS after the eligibility end");
    }

    [Fact]
    public async Task Future_start_grant_on_cloudpacs_is_denied_before_start_and_granted_after()
    {
        using var cts = ScenarioAppHostFixture.NewTestTimeout();
        var ct = cts.Token;
        var area = ScenarioAreas.AarhusDock;
        var admin = await fixture.ClientAsync(ScenarioPersonas.TenantAdmin, ct);
        var person = await ScenarioPeople.NewEmployeeWithCardAsync(admin, await fixture.ClientAsync(ScenarioPersonas.Manager, ct), ct);
        var start = DateTimeOffset.UtcNow.AddSeconds(150);
        var entitlement = await fixture.Access.RequestAndApproveAsync(person, area, start, start.AddDays(7), ct);

        // CloudPACS has no native validity: the grant is held until its start (DeferUntil), never applied early (§4.1).
        await Task.Delay(TimeSpan.FromSeconds(20), ct);
        Assert.DoesNotContain(await fixture.Pacs.CloudAssignmentsAsync(person.CardNumber, ct), a => a.GroupId == area.AccessLevelRef && a.Active);
        ScenarioAsserts.Denied(await fixture.Swipes.SwipeAsync(area.DoorRef, person.CardNumber, ct), "before the start");
        await Task.Delay(Max(TimeSpan.Zero, start - DateTimeOffset.UtcNow - TimeSpan.FromSeconds(10)), ct);
        Assert.DoesNotContain(await fixture.Pacs.CloudAssignmentsAsync(person.CardNumber, ct), a => a.GroupId == area.AccessLevelRef && a.Active);
        ScenarioAsserts.Denied(await fixture.Swipes.SwipeAsync(area.DoorRef, person.CardNumber, ct), "10 s before the start");

        await Task.Delay(Max(TimeSpan.Zero, start - DateTimeOffset.UtcNow), ct);
        var applied = await PacsProbe.WaitUntilAsync(
            async c => (await fixture.Pacs.CloudAssignmentsAsync(person.CardNumber, c)).Any(a => a.GroupId == area.AccessLevelRef && a.Active),
            TimeSpan.FromSeconds(60),
            ct);
        Assert.True(applied, "CloudPACS assignment not applied within 60 s after the start.");
        await fixture.Access.WaitForFulfillmentsAsync(entitlement, "Grant", "Confirmed", ScenarioAccess.ConfirmTimeout, ct);
        ScenarioAsserts.Granted(await fixture.Swipes.SwipeAsync(area.DoorRef, person.CardNumber, ct), "after the start");
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    private async Task AssertNoAssignmentsAsync(ScenarioPerson person, CancellationToken ct)
    {
        var legacyGone = await PacsProbe.WaitUntilAsync(
            async c => !(await fixture.Pacs.LegacyAssignmentsAsync(person.CardNumber, c)).Any(a => a.IsActive), RevokeTimeout, ct);
        var cloudGone = await PacsProbe.WaitUntilAsync(
            async c => !(await fixture.Pacs.CloudAssignmentsAsync(person.CardNumber, c)).Any(a => a.Active), RevokeTimeout, ct);
        Assert.True(legacyGone, "LegacyPACS still has an active assignment for the card.");
        Assert.True(cloudGone, "CloudPACS still has an active assignment for the card.");
    }

    /// <summary>Waits until no fulfillment of the entitlement is still Desired or Pending.</summary>
    private async Task WaitUntilSettledAsync(Guid entitlementId, CancellationToken ct)
    {
        var settled = await PacsProbe.WaitUntilAsync(
            async c => (await fixture.Db.FulfillmentsAsync(entitlementId, c)).All(f => f.Status is not ("Desired" or "Pending")), RevokeTimeout, ct);
        Assert.True(settled, $"Fulfillments of {entitlementId} still in flight after {RevokeTimeout}.");
    }
}
