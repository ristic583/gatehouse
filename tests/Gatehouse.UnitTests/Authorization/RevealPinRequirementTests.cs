using Gatehouse.Application.Authorization.Handlers;
using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Application.Authorization.Resources;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;
using Gatehouse.Domain.Tasks;
using static Gatehouse.UnitTests.Authorization.AuthTestKit;

namespace Gatehouse.UnitTests.Authorization;

public sealed class RevealPinRequirementTests
{
    private static StaffTaskResource Task(StaffTaskKind kind, PersonId? assignee, StaffTaskStatus status) => new(StaffTaskId.New(), kind, null, assignee, status);

    [Fact]
    public void Claimant_of_in_progress_pin_reveal_task_succeeds()
    {
        var me = PersonId.New();

        Assert.True(Run(new PinRevealTaskHandler(new FakeCurrentUser(me, staff: true)), new RevealPinRequirement(), Task(StaffTaskKind.PinReveal, me, StaffTaskStatus.InProgress)).HasSucceeded);
    }

    [Fact]
    public void Unclaimed_other_assignee_resolved_or_other_kind_fails()
    {
        var me = PersonId.New();
        var handler = new PinRevealTaskHandler(new FakeCurrentUser(me, staff: true));

        Assert.False(Run(handler, new RevealPinRequirement(), Task(StaffTaskKind.PinReveal, null, StaffTaskStatus.Open)).HasSucceeded);
        Assert.False(Run(handler, new RevealPinRequirement(), Task(StaffTaskKind.PinReveal, PersonId.New(), StaffTaskStatus.InProgress)).HasSucceeded);
        Assert.False(Run(handler, new RevealPinRequirement(), Task(StaffTaskKind.PinReveal, me, StaffTaskStatus.Resolved)).HasSucceeded);
        Assert.False(Run(handler, new RevealPinRequirement(), Task(StaffTaskKind.FulfillmentFailed, me, StaffTaskStatus.InProgress)).HasSucceeded);
        Assert.False(Run(handler, new HandleStaffTaskRequirement(), Task(StaffTaskKind.PinReveal, me, StaffTaskStatus.InProgress)).HasSucceeded);
    }

    [Fact]
    public void Claimant_whose_staff_scope_no_longer_covers_the_task_fails()
    {
        var me = PersonId.New();
        var tenantWide = Task(StaffTaskKind.PinReveal, me, StaffTaskStatus.InProgress);
        var atSite = new StaffTaskResource(StaffTaskId.New(), StaffTaskKind.PinReveal, AuthTestKit.Site, me, StaffTaskStatus.InProgress);
        var siteScoped = new FakeCurrentUser(me, staff: true, staffSites: new HashSet<SiteId> { SiteId.New() });

        Assert.False(Run(new PinRevealTaskHandler(siteScoped), new RevealPinRequirement(), tenantWide).HasSucceeded);
        Assert.False(Run(new PinRevealTaskHandler(siteScoped), new RevealPinRequirement(), atSite).HasSucceeded);
        Assert.False(Run(new PinRevealTaskHandler(new FakeCurrentUser(me)), new RevealPinRequirement(), tenantWide).HasSucceeded);
        Assert.True(Run(new PinRevealTaskHandler(new FakeCurrentUser(me, staff: true, staffSites: new HashSet<SiteId> { AuthTestKit.Site })), new RevealPinRequirement(), atSite).HasSucceeded);
    }

    [Fact]
    public void Tenant_admin_and_staff_scope_handlers_never_reveal()
    {
        var me = PersonId.New();
        var resource = Task(StaffTaskKind.PinReveal, me, StaffTaskStatus.InProgress);

        Assert.False(Run(new TenantAdminHandler(new FakeCurrentUser(me, admin: true)), new RevealPinRequirement(), resource).HasSucceeded);
        Assert.False(Run(new SecurityStaffScopeHandler(new FakeCurrentUser(me, staff: true)), new RevealPinRequirement(), resource).HasSucceeded);
    }
}
