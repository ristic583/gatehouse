using Gatehouse.Domain.Access;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;

namespace Gatehouse.Application.Access;

/// <summary>
/// Resolves the approver of each routed step (domain-model.md §2.4, approval routing). Pure. A manager step without an eligible manager or
/// sponsor goes to the first fallback approver (tenant admins) other than the subject and requester.
/// </summary>
public static class ApproverResolver
{
    public static Result<IReadOnlyList<StepPlan>> Resolve(
        IReadOnlyList<ApproverKind> kinds,
        PersonSnapshot subject,
        PersonId requestedById,
        PersonId? requesterManagerId,
        IReadOnlySet<PersonId> areaOwnerIds,
        IReadOnlyList<PersonId>? fallbackApproverIds = null)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(areaOwnerIds);

        var plans = new List<StepPlan>();
        foreach (var kind in kinds)
        {
            if (kind == ApproverKind.Manager)
            {
                var candidate = subject.ManagerId ?? subject.SponsorId;
                if (candidate == requestedById)
                {
                    candidate = requesterManagerId;
                }

                if (candidate is null || candidate == subject.Id || candidate == requestedById)
                {
                    // Top of the hierarchy (no manager): a tenant admin approves the manager step (lab finding #16).
                    candidate = fallbackApproverIds?.FirstOrDefault(f => f != subject.Id && f != requestedById) is { } fallback
                        && fallback != default
                        ? fallback
                        : null;
                }

                if (candidate is null)
                {
                    return NoApprover("No manager or sponsor can approve this request.");
                }

                plans.Add(new StepPlan(ApproverKind.Manager, candidate));
            }
            else
            {
                if (!areaOwnerIds.Any(o => o != subject.Id && o != requestedById))
                {
                    return NoApprover("No area owner other than the subject or requester can approve this request.");
                }

                plans.Add(new StepPlan(ApproverKind.AreaOwner, null));
            }
        }

        return plans;
    }

    private static Error NoApprover(string message) => Error.Validation("approval.no_eligible_approver", message);
}
