using Gatehouse.Domain.People;

namespace Gatehouse.Domain.Access;

public sealed record StepPlan(ApproverKind Kind, PersonId? AssignedApproverId);
