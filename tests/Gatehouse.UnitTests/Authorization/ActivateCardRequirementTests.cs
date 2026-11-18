using Gatehouse.Application.Authorization.Handlers;
using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Application.Authorization.Resources;
using Gatehouse.Domain.People;
using static Gatehouse.UnitTests.Authorization.AuthTestKit;

namespace Gatehouse.UnitTests.Authorization;

public sealed class ActivateCardRequirementTests
{
    private static PersonResource Subject(PersonId id, PersonType type, PersonId? manager = null, PersonId? sponsor = null) => new(id, type, manager, sponsor);

    [Fact]
    public void Employee_activates_own_card_only()
    {
        var me = PersonId.New();
        var handler = new SelfEmployeeHandler(new FakeCurrentUser(me));

        Assert.True(Run(handler, new ActivateCardRequirement(), Subject(me, PersonType.Employee)).HasSucceeded);
        Assert.False(Run(handler, new ActivateCardRequirement(), Subject(PersonId.New(), PersonType.Employee)).HasSucceeded);
        Assert.False(Run(handler, new ManagePersonRequirement(), Subject(me, PersonType.Employee)).HasSucceeded);
    }

    [Fact]
    public void External_visitor_and_guest_cannot_activate_own_card()
    {
        var me = PersonId.New();
        var handlers = new IAuthorizationHandlerLike[] { new(new SelfEmployeeHandler(new FakeCurrentUser(me))), new(new SelfHandler(new FakeCurrentUser(me))) };

        foreach (var type in new[] { PersonType.External, PersonType.Visitor, PersonType.Guest })
        {
            Assert.All(handlers, h => Assert.False(Run(h.Handler, new ActivateCardRequirement(), Subject(me, type)).HasSucceeded));
        }
    }

    [Fact]
    public void Manager_and_sponsor_activate_and_manage_their_people()
    {
        var me = PersonId.New();
        var manager = new TeamManagerHandler(new FakeCurrentUser(me));
        var sponsor = new SponsorHandler(new FakeCurrentUser(me));
        var report = Subject(PersonId.New(), PersonType.Employee, manager: me);
        var sponsored = Subject(PersonId.New(), PersonType.External, sponsor: me);
        var stranger = Subject(PersonId.New(), PersonType.External, PersonId.New(), PersonId.New());

        Assert.True(Run(manager, new ActivateCardRequirement(), report).HasSucceeded);
        Assert.True(Run(manager, new ManagePersonRequirement(), report).HasSucceeded);
        Assert.True(Run(sponsor, new ActivateCardRequirement(), sponsored).HasSucceeded);
        Assert.True(Run(sponsor, new ManagePersonRequirement(), sponsored).HasSucceeded);
        Assert.False(Run(manager, new ActivateCardRequirement(), stranger).HasSucceeded);
        Assert.False(Run(sponsor, new ActivateCardRequirement(), stranger).HasSucceeded);
    }

    [Fact]
    public void Tenant_admin_manages_people_but_never_activates()
    {
        var admin = new TenantAdminHandler(new FakeCurrentUser(PersonId.New(), admin: true));
        var person = Subject(PersonId.New(), PersonType.Employee);

        Assert.True(Run(admin, new ManagePersonRequirement(), person).HasSucceeded);
        Assert.False(Run(admin, new ActivateCardRequirement(), person).HasSucceeded);
        Assert.False(Run(new SecurityStaffScopeHandler(new FakeCurrentUser(PersonId.New(), staff: true)), new ActivateCardRequirement(), person).HasSucceeded);
    }

    private sealed record IAuthorizationHandlerLike(Microsoft.AspNetCore.Authorization.IAuthorizationHandler Handler);
}
