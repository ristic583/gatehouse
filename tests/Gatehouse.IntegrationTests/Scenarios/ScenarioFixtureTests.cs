using Gatehouse.Application.Identity;
using Gatehouse.IntegrationTests.Scenarios.Infrastructure;

namespace Gatehouse.IntegrationTests.Scenarios;

[Trait("Category", "AppHost")]
[Collection(ScenarioCollection.Name)]
public sealed class ScenarioFixtureTests(ScenarioAppHostFixture fixture)
{
    [Fact]
    public async Task Fixture_signs_in_every_persona()
    {
        using var cts = ScenarioAppHostFixture.NewTestTimeout();

        foreach (var email in ScenarioPersonas.All)
        {
            var client = await fixture.ClientAsync(email, cts.Token);
            var me = await client.GetAsync<MeDto>("v1/me", cts.Token);
            Assert.Equal(email, me.Email, ignoreCase: true);
            Assert.Equal("nordlys", me.TenantId);
        }
    }

    [Fact]
    public async Task Swipe_without_access_is_denied_on_both_pacs()
    {
        using var cts = ScenarioAppHostFixture.NewTestTimeout();
        var admin = await fixture.ClientAsync(ScenarioPersonas.TenantAdmin, cts.Token);
        var person = await ScenarioPeople.NewEmployeeWithCardAsync(admin, await fixture.ClientAsync(ScenarioPersonas.Manager, cts.Token), cts.Token);

        ScenarioAsserts.Denied(await fixture.Swipes.SwipeAsync(ScenarioAreas.HqLab.DoorRef, person.CardNumber, cts.Token), "LegacyPACS, no grant");
        ScenarioAsserts.Denied(await fixture.Swipes.SwipeAsync(ScenarioAreas.AarhusDock.DoorRef, person.CardNumber, cts.Token), "CloudPACS, no grant");
        Assert.Empty(await fixture.Pacs.LegacyAssignmentsAsync(person.CardNumber, cts.Token));
        Assert.Empty(await fixture.Pacs.CloudAssignmentsAsync(person.CardNumber, cts.Token));
    }

    [Fact]
    public async Task New_employee_card_is_active_on_both_pacs()
    {
        using var cts = ScenarioAppHostFixture.NewTestTimeout();
        var admin = await fixture.ClientAsync(ScenarioPersonas.TenantAdmin, cts.Token);
        var person = await ScenarioPeople.NewEmployeeWithCardAsync(admin, await fixture.ClientAsync(ScenarioPersonas.Manager, cts.Token), cts.Token);
        var now = DateTimeOffset.UtcNow;

        // A PACS learns a cardholder and card with the first grant on it (CardCommandTargets), so grant one area on each.
        await fixture.Access.GrantAsync(person, ScenarioAreas.HqLab, now, now.AddDays(7), cts.Token);
        await fixture.Access.GrantAsync(person, ScenarioAreas.AarhusDock, now, now.AddDays(7), cts.Token);

        Assert.Equal("Active", await fixture.Pacs.LegacyCardStateAsync(person.CardNumber, cts.Token));
        // CloudPACS lists are eventually consistent (ReadDelayMs), so poll briefly.
        string? cloudState = null;
        await PacsProbe.WaitUntilAsync(
            async c => (cloudState = await fixture.Pacs.CloudCardStateAsync(person.CardNumber, c)) == "active", TimeSpan.FromSeconds(15), cts.Token);
        Assert.Equal("active", cloudState);
    }

    [Fact]
    public async Task Grant_on_both_pacs_is_confirmed_and_swipe_is_granted()
    {
        using var cts = ScenarioAppHostFixture.NewTestTimeout();
        var admin = await fixture.ClientAsync(ScenarioPersonas.TenantAdmin, cts.Token);
        var person = await ScenarioPeople.NewEmployeeWithCardAsync(admin, await fixture.ClientAsync(ScenarioPersonas.Manager, cts.Token), cts.Token);
        var now = DateTimeOffset.UtcNow;

        var lab = await fixture.Access.GrantAsync(person, ScenarioAreas.HqLab, now, now.AddDays(7), cts.Token);
        var dock = await fixture.Access.GrantAsync(person, ScenarioAreas.AarhusDock, now, now.AddDays(7), cts.Token);

        Assert.All(await fixture.Db.FulfillmentsAsync(lab, cts.Token), f => Assert.Equal("Confirmed", f.Status));
        Assert.All(await fixture.Db.FulfillmentsAsync(dock, cts.Token), f => Assert.Equal("Confirmed", f.Status));
        Assert.Contains(await fixture.Pacs.LegacyAssignmentsAsync(person.CardNumber, cts.Token), a => a.AccessLevelRef == ScenarioAreas.HqLab.AccessLevelRef && a.IsActive);
        Assert.Contains(await fixture.Pacs.CloudAssignmentsAsync(person.CardNumber, cts.Token), a => a.GroupId == ScenarioAreas.AarhusDock.AccessLevelRef && a.Active);
        ScenarioAsserts.Granted(await fixture.Swipes.SwipeAsync(ScenarioAreas.HqLab.DoorRef, person.CardNumber, cts.Token), "LegacyPACS grant confirmed");
        ScenarioAsserts.Granted(await fixture.Swipes.SwipeAsync(ScenarioAreas.AarhusDock.DoorRef, person.CardNumber, cts.Token), "CloudPACS grant confirmed");
    }
}
