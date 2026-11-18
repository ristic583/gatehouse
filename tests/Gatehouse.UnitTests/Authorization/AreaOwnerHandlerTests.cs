using Gatehouse.Application.Authorization.Handlers;
using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.People;
using static Gatehouse.UnitTests.Authorization.AuthTestKit;

namespace Gatehouse.UnitTests.Authorization;

public sealed class AreaOwnerHandlerTests
{
    [Fact]
    public void Owner_may_approve_area_owner_step()
    {
        var me = PersonId.New();
        var subject = PersonId.New();
        var step = Step(Entitlement(Person(subject), subject, me), ApproverKind.AreaOwner);
        Assert.True(Run(new AreaOwnerHandler(new FakeCurrentUser(me)), new ApproveStepRequirement(), step).HasSucceeded);
    }

    [Fact]
    public void Owner_of_other_area_may_not_approve()
    {
        var me = PersonId.New();
        var subject = PersonId.New();
        var step = Step(Entitlement(Person(subject), subject, PersonId.New()), ApproverKind.AreaOwner);
        Assert.False(Run(new AreaOwnerHandler(new FakeCurrentUser(me)), new ApproveStepRequirement(), step).HasSucceeded);
    }

    [Fact]
    public void Owner_may_revoke_in_owned_area()
    {
        var me = PersonId.New();
        var handler = new AreaOwnerHandler(new FakeCurrentUser(me));
        var subject = PersonId.New();
        Assert.True(Run(handler, new Gatehouse.Application.Authorization.Requirements.RevokeEntitlementRequirement(), Entitlement(Person(subject), subject, me)).HasSucceeded);
    }

    [Fact]
    public void Owner_of_other_area_may_not_revoke()
    {
        var handler = new AreaOwnerHandler(new FakeCurrentUser(PersonId.New()));
        var subject = PersonId.New();
        Assert.False(Run(handler, new Gatehouse.Application.Authorization.Requirements.RevokeEntitlementRequirement(), Entitlement(Person(subject), subject, PersonId.New())).HasSucceeded);
    }
}
