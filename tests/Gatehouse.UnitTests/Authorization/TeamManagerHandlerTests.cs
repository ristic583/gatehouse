using Gatehouse.Application.Authorization.Handlers;
using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.People;
using static Gatehouse.UnitTests.Authorization.AuthTestKit;

namespace Gatehouse.UnitTests.Authorization;

public sealed class TeamManagerHandlerTests
{
    [Fact]
    public void Manager_may_request_for_report()
    {
        var me = PersonId.New();
        var handler = new TeamManagerHandler(new FakeCurrentUser(me));
        Assert.True(Run(handler, new RequestAccessRequirement(), Person(PersonId.New(), manager: me)).HasSucceeded);
        Assert.False(Run(handler, new RequestAccessRequirement(), Person(PersonId.New())).HasSucceeded);
    }

    [Fact]
    public void Manager_may_approve_assigned_manager_step()
    {
        var me = PersonId.New();
        var subject = PersonId.New();
        var handler = new TeamManagerHandler(new FakeCurrentUser(me));
        var entitlement = Entitlement(Person(subject, manager: me), subject);
        Assert.True(Run(handler, new ApproveStepRequirement(), Step(entitlement, ApproverKind.Manager, me)).HasSucceeded);
        Assert.False(Run(handler, new ApproveStepRequirement(), Step(entitlement, ApproverKind.Manager, PersonId.New())).HasSucceeded);
        Assert.False(Run(handler, new ApproveStepRequirement(), Step(entitlement, ApproverKind.AreaOwner)).HasSucceeded);
    }
}
