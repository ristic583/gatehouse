using Gatehouse.Domain.Access;
using Gatehouse.Domain.People;

namespace Gatehouse.Application.Authorization.Resources;

public sealed record ApprovalStepResource(
    EntitlementResource Entitlement, ApprovalStepId StepId, ApproverKind ApproverKind, PersonId? AssignedApproverId);
