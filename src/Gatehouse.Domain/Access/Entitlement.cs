using Gatehouse.Domain.Access.Events;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;

namespace Gatehouse.Domain.Access;

public sealed class Entitlement : AggregateRoot<EntitlementId>
{
    private readonly List<ApprovalStep> _steps = [];

    private Entitlement(
        EntitlementId id,
        PersonId personId,
        AreaId areaId,
        PersonId requestedById,
        DateTimeOffset requestedStart,
        DateTimeOffset requestedEnd,
        DateTimeOffset effectiveEnd,
        string justification,
        DateTimeOffset createdAt)
        : base(id)
    {
        PersonId = personId;
        AreaId = areaId;
        RequestedById = requestedById;
        RequestedStart = requestedStart;
        RequestedEnd = requestedEnd;
        EffectiveEnd = effectiveEnd;
        CappedToEligibility = effectiveEnd < requestedEnd;
        Justification = justification;
        Status = EntitlementStatus.PendingApproval;
        CreatedAt = createdAt;
    }

    // EF Core
    private Entitlement()
        : base(default)
    {
        Justification = string.Empty;
    }

    public PersonId PersonId { get; private set; }

    public AreaId AreaId { get; private set; }

    public PersonId RequestedById { get; private set; }

    public DateTimeOffset RequestedStart { get; private set; }

    public DateTimeOffset RequestedEnd { get; private set; }

    public DateTimeOffset EffectiveEnd { get; private set; }

    public bool CappedToEligibility { get; private set; }

    public string Justification { get; private set; }

    public EntitlementStatus Status { get; private set; }

    public IReadOnlyList<ApprovalStep> Steps => _steps.OrderBy(s => s.Order).ToList().AsReadOnly();

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public Revocation? Revocation { get; private set; }

    public uint Version { get; private set; }

    public static Entitlement Create(
        PersonId personId,
        AreaId areaId,
        PersonId requestedById,
        DateTimeOffset requestedStart,
        DateTimeOffset requestedEnd,
        DateTimeOffset effectiveEnd,
        string justification,
        IReadOnlyList<StepPlan> steps,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(steps);
        if (steps.Count == 0)
        {
            throw new ArgumentException("An entitlement needs at least one approval step.", nameof(steps));
        }

        var entitlement = new Entitlement(
            EntitlementId.New(), personId, areaId, requestedById, requestedStart, requestedEnd, effectiveEnd, justification, now);

        for (var i = 0; i < steps.Count; i++)
        {
            entitlement._steps.Add(new ApprovalStep(
                ApprovalStepId.New(),
                i + 1,
                steps[i].Kind,
                steps[i].AssignedApproverId,
                i == 0 ? ApprovalStepStatus.Pending : ApprovalStepStatus.Waiting));
        }

        entitlement.Raise(new AccessRequested(
            entitlement.Id, personId, areaId, requestedById, requestedStart, requestedEnd, effectiveEnd, entitlement.CappedToEligibility,
            now));
        entitlement.RaiseActivated(entitlement._steps[0], now);
        return entitlement;
    }

    public ApprovalStep? PendingStep => _steps.FirstOrDefault(s => s.Status == ApprovalStepStatus.Pending);

    public Result Approve(ApprovalStepId stepId, PersonId deciderId, string? comment, DateTimeOffset now)
    {
        var check = CheckDecision(stepId, deciderId);
        if (check.IsFailure)
        {
            return check.Error;
        }

        if (EffectiveEnd <= now)
        {
            // An approval can no longer give access for a period that is over (GH-ACCESS-02); the request can still be rejected or
            // cancelled.
            return Error.Conflict("approval.window_ended", "The requested access period has already ended.");
        }

        var step = check.Value;
        step.Decide(ApprovalStepStatus.Approved, deciderId, comment, now);
        Raise(new ApprovalGranted(Id, step.Id, step.ApproverKind, deciderId, comment, now));

        var next = _steps.Where(s => s.Status == ApprovalStepStatus.Waiting).OrderBy(s => s.Order).FirstOrDefault();
        if (next is not null)
        {
            next.Activate();
            RaiseActivated(next, now);
        }
        else
        {
            Status = EntitlementStatus.Approved;
            ApprovedAt = now;
            Raise(new EntitlementApproved(Id, PersonId, AreaId, RequestedStart, EffectiveEnd, now));
        }

        return Result.Success();
    }

    public Result Reject(ApprovalStepId stepId, PersonId deciderId, string comment, DateTimeOffset now)
    {
        var check = CheckDecision(stepId, deciderId);
        if (check.IsFailure)
        {
            return check.Error;
        }

        if (string.IsNullOrWhiteSpace(comment) || comment.Length > 500)
        {
            return Error.Validation("approval.comment_required", "Rejecting requires a comment (1-500 characters).");
        }

        var step = check.Value;
        step.Decide(ApprovalStepStatus.Rejected, deciderId, comment, now);
        foreach (var waiting in _steps.Where(s => s.Status == ApprovalStepStatus.Waiting))
        {
            waiting.Close();
        }

        Status = EntitlementStatus.Rejected;
        ClosedAt = now;
        Raise(new ApprovalRejected(Id, step.Id, deciderId, comment, now));
        Raise(new EntitlementRejected(Id, PersonId, AreaId, now));
        return Result.Success();
    }

    public Result Revoke(PersonId? by, string reason, RevocationKind kind, DateTimeOffset now)
    {
        if (Status != EntitlementStatus.Approved)
        {
            return InvalidTransition();
        }

        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
        {
            return Error.Validation("entitlement.revocation_reason_required", "Revoking requires a reason (1-500 characters).");
        }

        Revocation = new Revocation(by, now, reason, kind);
        Status = EntitlementStatus.Revoked;
        Raise(new EntitlementRevoked(Id, PersonId, AreaId, by, reason, kind, now));
        return Result.Success();
    }

    /// <summary>Closes an approved entitlement whose effective end has passed (lifecycle worker).</summary>
    public Result Expire(DateTimeOffset now)
    {
        if (Status != EntitlementStatus.Approved || EffectiveEnd > now)
        {
            return InvalidTransition();
        }

        Status = EntitlementStatus.Expired;
        ClosedAt = now;
        Raise(new EntitlementExpired(Id, PersonId, AreaId, now));
        return Result.Success();
    }

    /// <summary>
    /// Ends a request still pending when its window is over (lifecycle worker, GH-ACCESS-02): it can no longer be approved, so it closes
    /// its open approval steps and leaves the inboxes as <c>Expired</c> without a decision.
    /// </summary>
    public Result ExpireUnapproved(DateTimeOffset now)
    {
        if (Status != EntitlementStatus.PendingApproval || EffectiveEnd > now)
        {
            return InvalidTransition();
        }

        foreach (var step in _steps.Where(s => s.Status is ApprovalStepStatus.Pending or ApprovalStepStatus.Waiting))
        {
            step.Close();
        }

        Status = EntitlementStatus.Expired;
        ClosedAt = now;
        Raise(new EntitlementExpired(Id, PersonId, AreaId, now));
        return Result.Success();
    }

    /// <summary>Signals that a renewal request was opened for this entitlement (no state change).</summary>
    public void MarkRenewalDue(DateTimeOffset dueAt, DateTimeOffset now) => Raise(new RenewalDue(PersonId, Id, dueAt, now));

    public Result Cancel(PersonId by, DateTimeOffset now)
    {
        if (Status != EntitlementStatus.PendingApproval)
        {
            return InvalidTransition();
        }

        if (by != RequestedById)
        {
            return Error.Forbidden("entitlement.cancel_forbidden", "Only the requester can cancel this request.");
        }

        foreach (var step in _steps.Where(s => s.Status is ApprovalStepStatus.Pending or ApprovalStepStatus.Waiting))
        {
            step.Close();
        }

        Status = EntitlementStatus.Cancelled;
        ClosedAt = now;
        Raise(new EntitlementCancelled(Id, by, now));
        return Result.Success();
    }

    private static Error InvalidTransition() =>
        Error.Conflict("entitlement.invalid_transition", "The entitlement is not in a state that allows this change.");

    private Result<ApprovalStep> CheckDecision(ApprovalStepId stepId, PersonId deciderId)
    {
        if (Status != EntitlementStatus.PendingApproval)
        {
            return InvalidTransition();
        }

        var step = _steps.FirstOrDefault(s => s.Id == stepId);
        if (step is null)
        {
            return Error.NotFound("approval.step_not_found", "Approval step not found.");
        }

        if (step.Status != ApprovalStepStatus.Pending)
        {
            return Error.Conflict("approval.step_not_pending", "Only the pending step can be decided.");
        }

        if (deciderId == PersonId || deciderId == RequestedById)
        {
            return Error.Forbidden("approval.self_approval_forbidden", "The subject or requester cannot decide this request.");
        }

        return step;
    }

    private void RaiseActivated(ApprovalStep step, DateTimeOffset now) =>
        Raise(new ApprovalStepActivated(Id, step.Id, step.Order, step.ApproverKind, step.AssignedApproverId, now));
}
