using Gatehouse.Domain.Access;
using Gatehouse.Domain.People;

namespace Gatehouse.Application.Access.Notifications;

public sealed record NotifyApproversCommand(EntitlementId EntitlementId, ApprovalStepId StepId, PersonId? AssignedApproverId);
