using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;

namespace Gatehouse.Domain.Access.Events;

public sealed record ApprovalRejected(
    EntitlementId EntitlementId,
    ApprovalStepId StepId,
    PersonId DecidedById,
    string Comment,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
