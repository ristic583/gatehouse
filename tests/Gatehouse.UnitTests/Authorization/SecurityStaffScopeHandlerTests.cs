using Gatehouse.Application.Authorization.Handlers;
using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Application.Authorization.Resources;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;
using Gatehouse.Domain.Tasks;
using static Gatehouse.UnitTests.Authorization.AuthTestKit;

namespace Gatehouse.UnitTests.Authorization;

public sealed class SecurityStaffScopeHandlerTests
{
    [Fact]
    public void Site_scoped_staff_only_sees_their_sites()
    {
        var me = PersonId.New();
        var handler = new SecurityStaffScopeHandler(new FakeCurrentUser(me, staff: true, staffSites: new HashSet<SiteId> { AuthTestKit.Site }));
        var subject = Person(PersonId.New());
        var inScope = Entitlement(subject, subject.PersonId);
        var outOfScope = inScope with { Area = new AreaResource(AreaId.New(), SiteId.New(), new HashSet<PersonId>()) };

        Assert.True(Run(handler, new ViewEntitlementRequirement(), inScope).HasSucceeded);
        Assert.False(Run(handler, new ViewEntitlementRequirement(), outOfScope).HasSucceeded);
        Assert.False(Run(handler, new ApproveStepRequirement(), Step(inScope, ApproverKind.AreaOwner)).HasSucceeded);
    }

    private static FakeCurrentUser SiteStaff() =>
        new(PersonId.New(), staff: true, staffSites: new HashSet<SiteId> { AuthTestKit.Site });

    [Fact]
    public void Staff_may_emergency_revoke_in_scope_only()
    {
        var handler = new SecurityStaffScopeHandler(SiteStaff());
        var subject = PersonId.New();
        var inScope = Entitlement(Person(subject), subject);
        var outOfScope = inScope with { Area = new AreaResource(AreaId.New(), SiteId.New(), new HashSet<PersonId>()) };
        Assert.True(Run(handler, new EmergencyRevokeRequirement(), inScope).HasSucceeded);
        Assert.False(Run(handler, new EmergencyRevokeRequirement(), outOfScope).HasSucceeded);
    }

    [Fact]
    public void Site_scoped_staff_cannot_handle_tenant_wide_task()
    {
        var task = new StaffTaskResource(StaffTaskId.New(), StaffTaskKind.FulfillmentFailed, null, null, StaffTaskStatus.Open);
        Assert.False(Run(new SecurityStaffScopeHandler(SiteStaff()), new HandleStaffTaskRequirement(), task).HasSucceeded);
        var wide = new SecurityStaffScopeHandler(new FakeCurrentUser(PersonId.New(), staff: true));
        Assert.True(Run(wide, new HandleStaffTaskRequirement(), task).HasSucceeded);
        Assert.True(Run(new SecurityStaffScopeHandler(SiteStaff()), new HandleStaffTaskRequirement(), task with { SiteId = AuthTestKit.Site }).HasSucceeded);
    }

    [Fact]
    public void Site_scoped_staff_cannot_read_audit()
    {
        Assert.False(Run(new SecurityStaffScopeHandler(SiteStaff()), new AuditReaderRequirement(), null).HasSucceeded);
        Assert.True(Run(new SecurityStaffScopeHandler(SiteStaff()), new StaffConsoleRequirement(), null).HasSucceeded);
        Assert.True(Run(new SecurityStaffScopeHandler(new FakeCurrentUser(PersonId.New(), staff: true)), new AuditReaderRequirement(), null).HasSucceeded);
    }
}
