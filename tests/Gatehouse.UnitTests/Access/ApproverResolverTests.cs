using Gatehouse.Application.Access;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.People;

namespace Gatehouse.UnitTests.Access;

public sealed class ApproverResolverTests
{
    private static readonly IReadOnlyList<ApproverKind> ManagerOnly = [ApproverKind.Manager];
    private static readonly IReadOnlyList<ApproverKind> OwnerOnly = [ApproverKind.AreaOwner];

    [Fact]
    public void Manager_step_uses_manager()
    {
        var manager = PersonId.New();
        var subject = new PersonSnapshot(PersonId.New(), manager, null);
        var plan = ApproverResolver.Resolve(ManagerOnly, subject, subject.Id, manager, new HashSet<PersonId>()).Value;
        Assert.Equal(manager, Assert.Single(plan).AssignedApproverId);
    }

    [Fact]
    public void Manager_step_without_manager_goes_to_tenant_admin()
    {
        var admin = PersonId.New();
        var subject = new PersonSnapshot(PersonId.New(), null, null);
        var plan = ApproverResolver.Resolve(ManagerOnly, subject, subject.Id, null, new HashSet<PersonId>(), [subject.Id, admin]).Value;
        Assert.Equal(admin, Assert.Single(plan).AssignedApproverId);
    }

    [Fact]
    public void Manager_step_without_manager_or_other_admin_is_refused()
    {
        var subject = new PersonSnapshot(PersonId.New(), null, null);
        var result = ApproverResolver.Resolve(ManagerOnly, subject, subject.Id, null, new HashSet<PersonId>(), [subject.Id]);
        Assert.Equal("approval.no_eligible_approver", result.Error.Code);
    }

    [Fact]
    public void Manager_step_falls_back_to_sponsor()
    {
        var sponsor = PersonId.New();
        var subject = new PersonSnapshot(PersonId.New(), null, sponsor);
        var plan = ApproverResolver.Resolve(ManagerOnly, subject, subject.Id, null, new HashSet<PersonId>()).Value;
        Assert.Equal(sponsor, Assert.Single(plan).AssignedApproverId);
    }

    [Fact]
    public void Manager_requesting_for_report_escalates_to_their_manager()
    {
        var manager = PersonId.New();
        var managersManager = PersonId.New();
        var subject = new PersonSnapshot(PersonId.New(), manager, null);
        var plan = ApproverResolver.Resolve(ManagerOnly, subject, manager, managersManager, new HashSet<PersonId>()).Value;
        Assert.Equal(managersManager, Assert.Single(plan).AssignedApproverId);
    }

    [Fact]
    public void No_manager_and_no_sponsor_fails()
    {
        var subject = new PersonSnapshot(PersonId.New(), null, null);
        var result = ApproverResolver.Resolve(ManagerOnly, subject, subject.Id, null, new HashSet<PersonId>());
        Assert.Equal("approval.no_eligible_approver", result.Error.Code);
    }

    [Fact]
    public void Owner_step_excludes_subject_and_requester()
    {
        var requester = PersonId.New();
        var subject = new PersonSnapshot(PersonId.New(), requester, null);
        var result = ApproverResolver.Resolve(OwnerOnly, subject, requester, null, new HashSet<PersonId> { subject.Id, requester });
        Assert.Equal("approval.no_eligible_approver", result.Error.Code);

        var ok = ApproverResolver.Resolve(OwnerOnly, subject, requester, null, new HashSet<PersonId> { subject.Id, PersonId.New() }).Value;
        Assert.Null(Assert.Single(ok).AssignedApproverId);
    }

    [Fact]
    public void Sole_owner_requesting_own_area_fails()
    {
        var owner = new PersonSnapshot(PersonId.New(), PersonId.New(), null);
        var result = ApproverResolver.Resolve(OwnerOnly, owner, owner.Id, owner.ManagerId, new HashSet<PersonId> { owner.Id });
        Assert.Equal("approval.no_eligible_approver", result.Error.Code);
    }
}
