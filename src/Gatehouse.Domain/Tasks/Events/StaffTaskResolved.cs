using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;

namespace Gatehouse.Domain.Tasks.Events;

public sealed record StaffTaskResolved(
    StaffTaskId TaskId,
    PersonId? ResolvedById,
    string ResolutionAction,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
