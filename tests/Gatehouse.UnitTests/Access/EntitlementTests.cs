using Gatehouse.Domain.Access;
using Gatehouse.Domain.Access.Events;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;

namespace Gatehouse.UnitTests.Access;

public sealed class EntitlementTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);
    private readonly PersonId _subject = PersonId.New();
    private readonly PersonId _manager = PersonId.New();
    private readonly PersonId _owner = PersonId.New();

    private Entitlement TwoSteps(PersonId? requester = null, DateTimeOffset? effectiveEnd = null) =>
        Entitlement.Create(
            _subject,
            AreaId.New(),
            requester ?? _subject,
            Now.AddDays(1),
            Now.AddDays(30),
            effectiveEnd ?? Now.AddDays(30),
            "Need access for the project",
            [new StepPlan(ApproverKind.Manager, _manager), new StepPlan(ApproverKind.AreaOwner, null)],
            Now);

    [Fact]
    public void Create_activates_first_step_only()
    {
        var e = TwoSteps();
        Assert.Equal(EntitlementStatus.PendingApproval, e.Status);
        Assert.Equal(ApprovalStepStatus.Pending, e.Steps[0].Status);
        Assert.Equal(ApprovalStepStatus.Waiting, e.Steps[1].Status);
        Assert.IsType<AccessRequested>(e.DomainEvents[0]);
        var activated = Assert.IsType<ApprovalStepActivated>(Assert.Single(e.DomainEvents.OfType<ApprovalStepActivated>()));
        Assert.Equal(1, activated.Order);
    }

    [Fact]
    public void Approving_first_of_two_steps_activates_second()
    {
        var e = TwoSteps();
        Assert.True(e.Approve(e.Steps[0].Id, _manager, null, Now).IsSuccess);
        Assert.Equal(ApprovalStepStatus.Approved, e.Steps[0].Status);
        Assert.Equal(ApprovalStepStatus.Pending, e.Steps[1].Status);
        Assert.Equal(EntitlementStatus.PendingApproval, e.Status);
        Assert.Equal(2, e.DomainEvents.OfType<ApprovalStepActivated>().Count());
    }

    [Fact]
    public void Approving_last_step_approves_entitlement()
    {
        var e = TwoSteps();
        e.Approve(e.Steps[0].Id, _manager, null, Now);
        Assert.True(e.Approve(e.Steps[1].Id, _owner, "ok", Now.AddHours(1)).IsSuccess);
        Assert.Equal(EntitlementStatus.Approved, e.Status);
        Assert.Equal(Now.AddHours(1), e.ApprovedAt);
        Assert.Single(e.DomainEvents.OfType<EntitlementApproved>());
    }

    [Fact]
    public void Subject_cannot_approve()
    {
        var e = TwoSteps(requester: _manager);
        Assert.Equal("approval.self_approval_forbidden", e.Approve(e.Steps[0].Id, _subject, null, Now).Error.Code);
    }

    [Fact]
    public void Requester_cannot_approve()
    {
        var e = TwoSteps(requester: _manager);
        Assert.Equal("approval.self_approval_forbidden", e.Approve(e.Steps[0].Id, _manager, null, Now).Error.Code);
    }

    [Fact]
    public void Waiting_step_cannot_be_decided()
    {
        var e = TwoSteps();
        Assert.Equal("approval.step_not_pending", e.Approve(e.Steps[1].Id, _owner, null, Now).Error.Code);
    }

    [Fact]
    public void Reject_requires_comment()
    {
        var e = TwoSteps();
        Assert.Equal("approval.comment_required", e.Reject(e.Steps[0].Id, _manager, " ", Now).Error.Code);
        Assert.Equal(EntitlementStatus.PendingApproval, e.Status);
    }

    [Fact]
    public void Reject_closes_waiting_steps()
    {
        var e = TwoSteps();
        Assert.True(e.Reject(e.Steps[0].Id, _manager, "Not needed", Now).IsSuccess);
        Assert.Equal(EntitlementStatus.Rejected, e.Status);
        Assert.Equal(ApprovalStepStatus.Rejected, e.Steps[0].Status);
        Assert.Equal(ApprovalStepStatus.Closed, e.Steps[1].Status);
        Assert.Equal(Now, e.ClosedAt);
        Assert.Single(e.DomainEvents.OfType<EntitlementRejected>());
    }

    [Fact]
    public void Revoke_keeps_grant_fields()
    {
        var e = TwoSteps();
        e.Approve(e.Steps[0].Id, _manager, null, Now);
        e.Approve(e.Steps[1].Id, _owner, null, Now);
        var approvedAt = e.ApprovedAt;

        Assert.Equal("entitlement.revocation_reason_required", e.Revoke(_owner, "", RevocationKind.Normal, Now).Error.Code);
        Assert.True(e.Revoke(_owner, "Left project", RevocationKind.Normal, Now.AddDays(2)).IsSuccess);

        Assert.Equal(EntitlementStatus.Revoked, e.Status);
        Assert.Equal(approvedAt, e.ApprovedAt);
        Assert.Equal(Now.AddDays(30), e.EffectiveEnd);
        Assert.Equal(_manager, e.Steps[0].DecidedById);
        Assert.Equal("Left project", e.Revocation!.Reason);
        Assert.Equal("entitlement.invalid_transition", e.Revoke(_owner, "again", RevocationKind.Normal, Now).Error.Code);
    }

    [Fact]
    public void Effective_end_capped_sets_flag()
    {
        Assert.True(TwoSteps(effectiveEnd: Now.AddDays(10)).CappedToEligibility);
        Assert.False(TwoSteps().CappedToEligibility);
    }

    [Fact]
    public void Requester_can_cancel_pending_request()
    {
        var e = TwoSteps();
        Assert.True(e.Cancel(_subject, Now).IsSuccess);
        Assert.Equal(EntitlementStatus.Cancelled, e.Status);
        Assert.Equal(Now, e.ClosedAt);
        var ev = Assert.Single(e.DomainEvents.OfType<EntitlementCancelled>());
        Assert.Equal(_subject, ev.CancelledById);
    }

    [Fact]
    public void Cancel_by_other_person_is_forbidden()
    {
        var e = TwoSteps();
        var r = e.Cancel(_manager, Now);
        Assert.True(r.IsFailure);
        Assert.Equal("entitlement.cancel_forbidden", r.Error.Code);
        Assert.Equal(EntitlementStatus.PendingApproval, e.Status);
    }

    [Fact]
    public void Cancel_closes_pending_and_waiting_steps()
    {
        var e = TwoSteps();
        e.Cancel(_subject, Now);
        Assert.All(e.Steps, s => Assert.Equal(ApprovalStepStatus.Closed, s.Status));
    }

    [Fact]
    public void Approved_entitlement_cannot_be_cancelled()
    {
        var e = TwoSteps();
        e.Approve(e.Steps[0].Id, _manager, null, Now);
        e.Approve(e.Steps[1].Id, _owner, null, Now);
        var r = e.Cancel(_subject, Now);
        Assert.True(r.IsFailure);
        Assert.Equal("entitlement.invalid_transition", r.Error.Code);
    }

    [Fact]
    public void Expire_requires_approved_and_past_end()
    {
        var pending = TwoSteps();
        Assert.Equal("entitlement.invalid_transition", pending.Expire(Now.AddDays(40)).Error.Code);

        var e = TwoSteps();
        e.Approve(e.Steps[0].Id, _manager, null, Now);
        e.Approve(e.Steps[1].Id, _owner, null, Now);
        Assert.Equal("entitlement.invalid_transition", e.Expire(Now.AddDays(29)).Error.Code);

        Assert.True(e.Expire(Now.AddDays(30)).IsSuccess);
        Assert.Equal(EntitlementStatus.Expired, e.Status);
        Assert.Equal(Now.AddDays(30), e.ClosedAt);
        Assert.Single(e.DomainEvents.OfType<EntitlementExpired>());
    }

    [Fact]
    public void Pending_request_expires_after_window_end()
    {
        var e = TwoSteps();
        e.Approve(e.Steps[0].Id, _manager, null, Now);
        Assert.Equal("entitlement.invalid_transition", e.ExpireUnapproved(Now.AddDays(29)).Error.Code);

        Assert.True(e.ExpireUnapproved(Now.AddDays(30)).IsSuccess);

        Assert.Equal((EntitlementStatus.Expired, Now.AddDays(30)), (e.Status, e.ClosedAt));
        Assert.Equal([ApprovalStepStatus.Approved, ApprovalStepStatus.Closed], e.Steps.Select(s => s.Status));
        Assert.Single(e.DomainEvents.OfType<EntitlementExpired>());
    }

    [Fact]
    public void Approved_request_is_not_expired_unapproved()
    {
        var e = TwoSteps();
        e.Approve(e.Steps[0].Id, _manager, null, Now);
        e.Approve(e.Steps[1].Id, _owner, null, Now);

        Assert.Equal("entitlement.invalid_transition", e.ExpireUnapproved(Now.AddDays(40)).Error.Code);
        Assert.Equal(EntitlementStatus.Approved, e.Status);
    }

    [Fact]
    public void Approve_after_window_ended_is_rejected()
    {
        var e = TwoSteps();
        e.Approve(e.Steps[0].Id, _manager, null, Now);

        var late = e.Approve(e.Steps[1].Id, _owner, null, Now.AddDays(30));

        Assert.Equal("approval.window_ended", late.Error.Code);
        Assert.Equal(EntitlementStatus.PendingApproval, e.Status);
        Assert.Equal(ApprovalStepStatus.Pending, e.Steps[1].Status);
        Assert.Empty(e.DomainEvents.OfType<EntitlementApproved>());
    }
}
