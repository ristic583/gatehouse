using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;

namespace Gatehouse.Domain.Access.Events;

public sealed record ApprovalStepActivated(
    EntitlementId EntitlementId,
    ApprovalStepId StepId,
    int Order,
    ApproverKind ApproverKind,
    PersonId? AssignedApproverId,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
