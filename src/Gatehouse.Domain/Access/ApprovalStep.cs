using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;

namespace Gatehouse.Domain.Access;

public sealed class ApprovalStep : Entity<ApprovalStepId>
{
    internal ApprovalStep(ApprovalStepId id, int order, ApproverKind approverKind, PersonId? assignedApproverId, ApprovalStepStatus status)
        : base(id)
    {
        Order = order;
        ApproverKind = approverKind;
        AssignedApproverId = assignedApproverId;
        Status = status;
    }

    // EF Core
    private ApprovalStep()
        : base(default)
    {
    }

    public int Order { get; private set; }

    public ApproverKind ApproverKind { get; private set; }

    public PersonId? AssignedApproverId { get; private set; }

    public ApprovalStepStatus Status { get; private set; }

    public PersonId? DecidedById { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    public string? Comment { get; private set; }

    internal void Activate() => Status = ApprovalStepStatus.Pending;

    internal void Close() => Status = ApprovalStepStatus.Closed;

    internal void Decide(ApprovalStepStatus status, PersonId deciderId, string? comment, DateTimeOffset now)
    {
        Status = status;
        DecidedById = deciderId;
        DecidedAt = now;
        Comment = comment;
    }
}
