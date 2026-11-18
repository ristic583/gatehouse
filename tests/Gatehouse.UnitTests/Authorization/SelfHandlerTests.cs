using Gatehouse.Application.Authorization.Handlers;
using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Domain.People;
using static Gatehouse.UnitTests.Authorization.AuthTestKit;

namespace Gatehouse.UnitTests.Authorization;

public sealed class SelfHandlerTests
{
    [Fact]
    public void Subject_may_request_for_self()
    {
        var me = PersonId.New();
        var handler = new SelfHandler(new FakeCurrentUser(me));
        Assert.True(Run(handler, new RequestAccessRequirement(), Person(me)).HasSucceeded);
        Assert.False(Run(handler, new RequestAccessRequirement(), Person(PersonId.New())).HasSucceeded);
    }
}
