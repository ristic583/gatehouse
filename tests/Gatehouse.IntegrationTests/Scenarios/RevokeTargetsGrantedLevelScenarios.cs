using Gatehouse.IntegrationTests.Scenarios.Infrastructure;

namespace Gatehouse.IntegrationTests.Scenarios;

/// <summary>Scenarios 1-2 (improvements.md §A2, conventions.md §4.2): a revoke removes the level that was granted.</summary>
[Trait("Category", "AppHost")]
[Collection(ScenarioCollection.Name)]
public sealed class RevokeTargetsGrantedLevelScenarios(ScenarioAppHostFixture fixture)
{
    /// <summary>A level the LegacyPACS simulator knows (door D-CPH-PLANT) that no demo area is mapped to.</summary>
    private const string OtherLevel = "AL-CPH-PLANT";
    private const string OtherDoor = "D-CPH-PLANT";

    [Fact]
    public async Task Revoke_after_mapping_edit_removes_originally_granted_level()
    {
        using var cts = ScenarioAppHostFixture.NewTestTimeout();
        var ct = cts.Token;
        var area = ScenarioAreas.HqLab;
        var admin = await fixture.ClientAsync(ScenarioPersonas.TenantAdmin, ct);
        var first = await ScenarioPeople.NewEmployeeWithCardAsync(admin, await fixture.ClientAsync(ScenarioPersonas.Manager, ct), ct);
        var second = await ScenarioPeople.NewEmployeeWithCardAsync(admin, await fixture.ClientAsync(ScenarioPersonas.Manager, ct), ct);
        var now = DateTimeOffset.UtcNow;
        var entitlement = await fixture.Access.GrantAsync(first, area, now, now.AddDays(7), ct);

        await using (await fixture.Mappings.RemapAsync(area, OtherLevel, ct))
        {
            await fixture.Access.GrantAsync(second, area, now, now.AddDays(7), ct);
            Assert.Contains(await fixture.Pacs.LegacyAssignmentsAsync(second.CardNumber, ct), a => a.AccessLevelRef == OtherLevel && a.IsActive);

            var owner = await fixture.ClientAsync(ScenarioPersonas.AreaOwner, ct);
            await ScenarioAccess.RevokeAsync(owner, entitlement, ct);
            await fixture.Access.WaitForFulfillmentsAsync(entitlement, "Revoke", "Confirmed", ScenarioAccess.ConfirmTimeout, ct);
        }

        Assert.DoesNotContain(await fixture.Pacs.LegacyAssignmentsAsync(first.CardNumber, ct), a => a.AccessLevelRef == area.AccessLevelRef && a.IsActive);
        ScenarioAsserts.Denied(await fixture.Swipes.SwipeAsync(area.DoorRef, first.CardNumber, ct), "originally granted level revoked after the remap");
        Assert.Contains(await fixture.Pacs.LegacyAssignmentsAsync(second.CardNumber, ct), a => a.AccessLevelRef == OtherLevel && a.IsActive);
        ScenarioAsserts.Granted(await fixture.Swipes.SwipeAsync(OtherDoor, second.CardNumber, ct), "the new level of another person is untouched");
    }

    [Fact]
    public async Task Revoke_after_mapping_deleted_still_removes_granted_level()
    {
        using var cts = ScenarioAppHostFixture.NewTestTimeout();
        var ct = cts.Token;
        var area = ScenarioAreas.HqLab;
        var admin = await fixture.ClientAsync(ScenarioPersonas.TenantAdmin, ct);
        var person = await ScenarioPeople.NewEmployeeWithCardAsync(admin, await fixture.ClientAsync(ScenarioPersonas.Manager, ct), ct);
        var now = DateTimeOffset.UtcNow;
        var entitlement = await fixture.Access.GrantAsync(person, area, now, now.AddDays(7), ct);
        ScenarioAsserts.Granted(await fixture.Swipes.SwipeAsync(area.DoorRef, person.CardNumber, ct), "grant confirmed");

        await using (await fixture.Mappings.DeleteAsync(area, ct))
        {
            var owner = await fixture.ClientAsync(ScenarioPersonas.AreaOwner, ct);
            await ScenarioAccess.RevokeAsync(owner, entitlement, ct);
            await fixture.Access.WaitForFulfillmentsAsync(entitlement, "Revoke", "Confirmed", ScenarioAccess.ConfirmTimeout, ct);
        }

        Assert.DoesNotContain(await fixture.Pacs.LegacyAssignmentsAsync(person.CardNumber, ct), a => a.AccessLevelRef == area.AccessLevelRef && a.IsActive);
        ScenarioAsserts.Denied(await fixture.Swipes.SwipeAsync(area.DoorRef, person.CardNumber, ct), "granted level revoked although its mapping is gone");
    }
}
