using System.Security.Claims;
using Gatehouse.Application.Access.Dtos;
using Gatehouse.Application.Access.GetAccessRequest;
using Gatehouse.Application.Audit;
using Gatehouse.Application.Authorization.Handlers;
using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Application.Authorization.Resources;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Identity;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using FulfillmentEntity = Gatehouse.Domain.Fulfillment.Fulfillment;

namespace Gatehouse.Application.Access.Approvals;

/// <summary>Shared flow of approve/reject: load by step, authorize, decide, create fulfillments, save once.</summary>
public sealed class StepDecisionService(
    ITenantDb db,
    ICurrentUser currentUser,
    IAuthorizationService authorization,
    EntitlementResourceLoader resources,
    AccessRequestDtoBuilder dtos,
    IAuditLog audit,
    TimeProvider time)
{
    /// <summary>One grant fulfillment per entitlement and PACS instance; a concurrent approval of the same step trips it.</summary>
    public const string FulfillmentUniqueIndex = "ix_fulfillments_entitlement_id_pacs_instance_id_kind";

    public async Task<Result<AccessRequestDto>> DecideAsync(
        DecideStepCommand command,
        Func<Entitlement, ApprovalStepId, PersonId, DateTimeOffset, Result> decide,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(decide);
        var me = await currentUser.RequirePersonIdAsync(ct);
        var now = time.GetUtcNow();
        var stepId = new ApprovalStepId(command.StepId);

        var entitlement = await db.Entitlements.SingleOrDefaultAsync(e => e.Steps.Any(s => s.Id == stepId), ct);
        if (entitlement is null)
        {
            return Error.NotFound("approval.step_not_found", "Approval step not found.");
        }

        var step = entitlement.Steps.Single(s => s.Id == stepId);
        var resource = new ApprovalStepResource(
            await resources.LoadAsync(entitlement, ct), step.Id, step.ApproverKind, step.AssignedApproverId);
        var allowed = await authorization.AuthorizeAsync(new ClaimsPrincipal(), resource, new ApproveStepRequirement());
        if (!allowed.Succeeded)
        {
            var selfApproval = allowed.Failure?.FailureReasons.Any(r => r.Handler is NoSelfApprovalHandler) == true;
            return selfApproval
                ? Error.Forbidden("approval.self_approval_forbidden", "The subject or requester cannot decide this request.")
                : Error.Forbidden("approval.not_an_approver", "You are not an approver of this step.");
        }

        var decided = decide(entitlement, stepId, me, now);
        if (decided.IsFailure)
        {
            return decided.Error;
        }

        var decidedStep = entitlement.Steps.Single(s => s.Id == stepId);
        audit.Record(
            AuditActor.ForPerson(me),
            decidedStep.Status == ApprovalStepStatus.Rejected ? "approval.rejected" : "approval.granted",
            AuditSubjects.Entitlement,
            entitlement.Id.Value.ToString(),
            new { stepId = stepId.Value, approverKind = decidedStep.ApproverKind.ToString(), comment = decidedStep.Comment });

        if (entitlement.Status == EntitlementStatus.Approved)
        {
            var fulfillmentIds = new List<Guid>();
            var mappings = await db.AccessLevelMappings.AsNoTracking()
                .Where(m => m.AreaId == entitlement.AreaId)
                .Select(m => m.PacsInstanceId)
                .ToListAsync(ct);
            // Planned in-process by design (ADR-008, M4 outcome)
            foreach (var instance in mappings.Distinct())
            {
                var fulfillment = FulfillmentEntity.Create(entitlement.Id, instance, FulfillmentKind.Grant, CommandPriority.Normal, now);
                db.Fulfillments.Add(fulfillment);
                fulfillmentIds.Add(fulfillment.Id.Value);
            }

            audit.Record(
                AuditActor.ForPerson(me), "entitlement.approved", AuditSubjects.Entitlement, entitlement.Id.Value.ToString(),
                new { fulfillmentIds });
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return StepDecidedElsewhere();
        }
        catch (DbUpdateException ex) when (db.IsUniqueViolation(ex, FulfillmentUniqueIndex))
        {
            // The race loser may trip the fulfillment index before the step's row version (M11-41); the save's
            // transaction was rolled back, so neither its decision, audit entries nor outbox messages were written.
            return StepDecidedElsewhere();
        }

        return (await dtos.BuildAsync([entitlement], ct))[0];
    }

    private static Error StepDecidedElsewhere() =>
        Error.Conflict("approval.step_not_pending", "The step was decided by someone else.");
}
