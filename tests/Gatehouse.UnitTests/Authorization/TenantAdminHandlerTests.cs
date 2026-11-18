using Gatehouse.Application.Authorization.Handlers;
using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Application.Authorization.Resources;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;
using Gatehouse.Domain.Tasks;
using static Gatehouse.UnitTests.Authorization.AuthTestKit;

namespace Gatehouse.UnitTests.Authorization;

public sealed class TenantAdminHandlerTests
{
    [Fact]
    public void Admin_cannot_approve_by_role()
    {
        var me = PersonId.New();
        var subject = PersonId.New();
        var handler = new TenantAdminHandler(new FakeCurrentUser(me, admin: true));
        var entitlement = Entitlement(Person(subject), subject, PersonId.New());

        Assert.False(Run(handler, new ApproveStepRequirement(), Step(entitlement, ApproverKind.AreaOwner)).HasSucceeded);
        Assert.False(Run(handler, new ApproveStepRequirement(), Step(entitlement, ApproverKind.Manager, PersonId.New())).HasSucceeded);
        Assert.True(Run(handler, new ManageTenantRequirement(), null).HasSucceeded);
        Assert.True(Run(handler, new ViewEntitlementRequirement(), entitlement).HasSucceeded);
    }

    [Fact]
    public void Admin_cannot_revoke_or_handle_fulfillment()
    {
        var me = PersonId.New();
        var handler = new TenantAdminHandler(new FakeCurrentUser(me, admin: true));
        var subject = PersonId.New();
        var e = Entitlement(Person(subject), subject, PersonId.New());
        Assert.False(Run(handler, new RevokeEntitlementRequirement(), e).HasSucceeded);
        Assert.False(Run(handler, new EmergencyRevokeRequirement(), e).HasSucceeded);
        Assert.False(Run(handler, new HandleFulfillmentRequirement(), new FulfillmentResource(FulfillmentId.New(), AuthTestKit.Site, subject, subject)).HasSucceeded);
        Assert.False(Run(handler, new HandleStaffTaskRequirement(), new StaffTaskResource(StaffTaskId.New(), StaffTaskKind.FulfillmentFailed, null, null, StaffTaskStatus.Open)).HasSucceeded);
    }

    [Fact]
    public void Admin_may_open_console_and_read_audit()
    {
        var handler = new TenantAdminHandler(new FakeCurrentUser(PersonId.New(), admin: true));
        Assert.True(Run(handler, new StaffConsoleRequirement(), null).HasSucceeded);
        Assert.True(Run(handler, new AuditReaderRequirement(), null).HasSucceeded);
    }
}
