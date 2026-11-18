using Gatehouse.Application.Authorization.Handlers;
using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Application.Authorization.Resources;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;
using static Gatehouse.UnitTests.Authorization.AuthTestKit;

namespace Gatehouse.UnitTests.Authorization;

public sealed class NoSelfFulfillmentHandlerTests
{
    [Fact]
    public void Staff_cannot_handle_own_entitlement()
    {
        var me = PersonId.New();
        var handler = new NoSelfFulfillmentHandler(new FakeCurrentUser(me, staff: true));
        var res = new FulfillmentResource(FulfillmentId.New(), Site, me, PersonId.New());
        Assert.True(Run(handler, new HandleFulfillmentRequirement(), res).HasFailed);
    }

    [Fact]
    public void Staff_cannot_handle_entitlement_they_requested()
    {
        var me = PersonId.New();
        var handler = new NoSelfFulfillmentHandler(new FakeCurrentUser(me, staff: true));
        var res = new FulfillmentResource(FulfillmentId.New(), Site, PersonId.New(), me);
        Assert.True(Run(handler, new HandleFulfillmentRequirement(), res).HasFailed);
        var other = new FulfillmentResource(FulfillmentId.New(), Site, PersonId.New(), PersonId.New());
        Assert.False(Run(handler, new HandleFulfillmentRequirement(), other).HasFailed);
    }
}
