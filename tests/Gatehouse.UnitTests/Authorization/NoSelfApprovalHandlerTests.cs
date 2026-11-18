using Gatehouse.Application.Authorization.Handlers;
using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.People;
using Microsoft.AspNetCore.Authorization;
using static Gatehouse.UnitTests.Authorization.AuthTestKit;

namespace Gatehouse.UnitTests.Authorization;

public sealed class NoSelfApprovalHandlerTests
{
    private static AuthorizationHandlerContext RunAll(PersonId me, object resource)
    {
        var user = new FakeCurrentUser(me);
        var context = new AuthorizationHandlerContext([new ApproveStepRequirement()], new System.Security.Claims.ClaimsPrincipal(), resource);
        foreach (IAuthorizationHandler handler in new IAuthorizationHandler[] { new AreaOwnerHandler(user), new TeamManagerHandler(user), new NoSelfApprovalHandler(user) })
        {
            handler.HandleAsync(context).GetAwaiter().GetResult();
        }

        return context;
    }

    [Fact]
    public void Requester_fails_even_if_owner()
    {
        var me = PersonId.New();
        var step = Step(Entitlement(Person(PersonId.New()), me, me), ApproverKind.AreaOwner);
        var context = RunAll(me, step);
        Assert.True(context.HasFailed);
        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public void Subject_fails_even_if_manager()
    {
        var me = PersonId.New();
        var requester = PersonId.New();
        var step = Step(Entitlement(Person(me, manager: me), requester), ApproverKind.Manager, me);
        var context = RunAll(me, step);
        Assert.True(context.HasFailed);
        Assert.False(context.HasSucceeded);
    }
}
