using Gatehouse.IntegrationTests.Scenarios.Infrastructure;

namespace Gatehouse.IntegrationTests.Scenarios;

/// <summary>Scenarios 12, 13 (improvements.md §A2, conventions.md §4.4): every end of a person's access reaches both PACS.</summary>
[Trait("Category", "AppHost")]
[Collection(ScenarioCollection.Name)]
public sealed class CardAndTerminationScenarios(ScenarioAppHostFixture fixture)
{
    private static readonly TimeSpan EffectTimeout = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task Staff_card_deactivation_is_denied_on_both_pacs_within_one_minute()
    {
        using var cts = ScenarioAppHostFixture.NewTestTimeout();
        var ct = cts.Token;
        var admin = await fixture.ClientAsync(ScenarioPersonas.TenantAdmin, ct);
        var person = await ScenarioPeople.NewEmployeeWithCardAsync(admin, await fixture.ClientAsync(ScenarioPersonas.Manager, ct), ct);
        await GrantBothAsync(person, ct);

        var staff = await fixture.ClientAsync(ScenarioPersonas.SecurityStaff, ct);
        using (var response = await staff.PostAsync($"v1/cards/{person.CardId}/deactivate", new { reason = "M11 scenario 12: reported lost" }, ct))
        {
            Assert.True(response.IsSuccessStatusCode, $"deactivate: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(ct)}");
        }

        await AssertDeniedOnBothAsync(person, "card deactivated by security staff", ct);
    }

    [Fact]
    public async Task Ending_sponsorship_revokes_access_and_cards_on_both_pacs()
    {
        using var cts = ScenarioAppHostFixture.NewTestTimeout();
        var ct = cts.Token;
        var sponsor = await fixture.ClientAsync(ScenarioPersonas.Manager, ct);
        var person = await ScenarioPeople.NewSponsoredExternalWithCardAsync(sponsor, ct);
        await GrantBothAsync(person, ct);

        using (var response = await sponsor.PostAsync($"v1/people/{person.PersonId}/sponsorship/end", null, ct))
        {
            Assert.True(response.IsSuccessStatusCode, $"end sponsorship: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(ct)}");
        }

        await AssertRevokedEverywhereAsync(person, "sponsorship ended", ct);
    }

    [Fact]
    public async Task Terminating_employee_revokes_access_and_cards_on_both_pacs()
    {
        using var cts = ScenarioAppHostFixture.NewTestTimeout();
        var ct = cts.Token;
        var admin = await fixture.ClientAsync(ScenarioPersonas.TenantAdmin, ct);
        var person = await ScenarioPeople.NewEmployeeWithCardAsync(admin, await fixture.ClientAsync(ScenarioPersonas.Manager, ct), ct);
        await GrantBothAsync(person, ct);

        using (var response = await admin.PostAsync($"v1/admin/people/{person.PersonId}/terminate", null, ct))
        {
            Assert.True(response.IsSuccessStatusCode, $"terminate: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(ct)}");
        }

        await AssertRevokedEverywhereAsync(person, "employee terminated", ct);
    }

    private async Task GrantBothAsync(ScenarioPerson person, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        await fixture.Access.GrantAsync(person, ScenarioAreas.HqLab, now, now.AddDays(7), ct);
        await fixture.Access.GrantAsync(person, ScenarioAreas.AarhusDock, now, now.AddDays(7), ct);
        ScenarioAsserts.Granted(await fixture.Swipes.SwipeAsync(ScenarioAreas.HqLab.DoorRef, person.CardNumber, ct), "LegacyPACS grant confirmed");
        ScenarioAsserts.Granted(await fixture.Swipes.SwipeAsync(ScenarioAreas.AarhusDock.DoorRef, person.CardNumber, ct), "CloudPACS grant confirmed");
    }

    private async Task AssertRevokedEverywhereAsync(ScenarioPerson person, string because, CancellationToken ct)
    {
        var legacyGone = await PacsProbe.WaitUntilAsync(
            async c => !(await fixture.Pacs.LegacyAssignmentsAsync(person.CardNumber, c)).Any(a => a.IsActive), EffectTimeout, ct);
        var cloudGone = await PacsProbe.WaitUntilAsync(
            async c => !(await fixture.Pacs.CloudAssignmentsAsync(person.CardNumber, c)).Any(a => a.Active), EffectTimeout, ct);
        Assert.True(legacyGone, $"LegacyPACS still has an active assignment ({because}).");
        Assert.True(cloudGone, $"CloudPACS still has an active assignment ({because}).");

        string? legacyCard = null;
        string? cloudCard = null;
        var cardsBlocked = await PacsProbe.WaitUntilAsync(
            async c =>
            {
                legacyCard = await fixture.Pacs.LegacyCardStateAsync(person.CardNumber, c);
                cloudCard = await fixture.Pacs.CloudCardStateAsync(person.CardNumber, c);
                return legacyCard is not null && legacyCard != "Active" && cloudCard is not null && cloudCard != "active";
            },
            EffectTimeout,
            ct);
        Assert.True(cardsBlocked, $"Card not blocked on both PACS ({because}): LegacyPACS {legacyCard}, CloudPACS {cloudCard}.");
        await AssertDeniedOnBothAsync(person, because, ct);
    }

    private async Task AssertDeniedOnBothAsync(ScenarioPerson person, string because, CancellationToken ct)
    {
        foreach (var door in new[] { ScenarioAreas.HqLab.DoorRef, ScenarioAreas.AarhusDock.DoorRef })
        {
            var denied = await PacsProbe.WaitUntilAsync(
                async c => (await fixture.Swipes.SwipeAsync(door, person.CardNumber, c)).Decision == "Denied", EffectTimeout, ct);
            Assert.True(denied, $"Swipe at {door} still granted after {EffectTimeout} ({because}).");
        }
    }
}
