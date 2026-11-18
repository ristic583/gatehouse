using Gatehouse.Application.Authorization.Handlers;
using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Domain.People;
using static Gatehouse.UnitTests.Authorization.AuthTestKit;

namespace Gatehouse.UnitTests.Authorization;

public sealed class SponsorHandlerTests
{
    [Fact]
    public void Sponsor_may_request_for_sponsored()
    {
        var me = PersonId.New();
        var handler = new SponsorHandler(new FakeCurrentUser(me));
        Assert.True(Run(handler, new RequestAccessRequirement(), Person(PersonId.New(), sponsor: me)).HasSucceeded);
        Assert.False(Run(handler, new RequestAccessRequirement(), Person(PersonId.New(), sponsor: PersonId.New())).HasSucceeded);
    }
}
