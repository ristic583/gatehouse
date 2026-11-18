using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;

namespace Gatehouse.Domain.Access.Events;

public sealed record ApprovalGranted(
    EntitlementId EntitlementId,
    ApprovalStepId StepId,
    ApproverKind ApproverKind,
    PersonId DecidedById,
    string? Comment,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
