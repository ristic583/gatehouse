using System.Security.Claims;
using Gatehouse.Application.Access.Dtos;
using Gatehouse.Application.Audit;
using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Application.Authorization.Resources;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Identity;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Gatehouse.Application.Access.RequestAccess;

public sealed class RequestAccessHandler(
    ITenantDb db,
    ICurrentUser currentUser,
    IAuthorizationService authorization,
    AccessRequestDtoBuilder dtos,
    IAuditLog audit,
    TimeProvider time,
    ILogger<RequestAccessHandler> logger)
{
    public async Task<Result<AccessRequestDto>> HandleAsync(RequestAccessCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var request = command.Request;
        var me = await currentUser.RequirePersonIdAsync(ct);
        var now = time.GetUtcNow();
        var subjectId = request.PersonId is { } pid ? new PersonId(pid) : me;

        var people = await db.Persons.AsNoTracking().Where(p => p.Id == subjectId || p.Id == me).ToListAsync(ct);
        var subject = people.SingleOrDefault(p => p.Id == subjectId);
        if (subject is null)
        {
            return Error.NotFound("person.not_found", "Person not found.");
        }

        var requester = people.Single(p => p.Id == me);
        var allowed = await authorization.AuthorizeAsync(
            new ClaimsPrincipal(),
            new PersonResource(subject.Id, subject.Type, subject.ManagerId, subject.SponsorId),
            new RequestAccessRequirement());
        if (!allowed.Succeeded)
        {
            return Error.Forbidden("request.forbidden_for_subject", "You may not request access for this person.");
        }

        var areaId = new AreaId(request.AreaId);
        var area = await db.Areas.AsNoTracking().SingleOrDefaultAsync(a => a.Id == areaId, ct);
        if (area is null)
        {
            return Error.NotFound("area.not_found", "Area not found.");
        }

        if (!area.AccessControlled)
        {
            return Error.Validation("request.area_not_access_controlled", "This area has no access control; no request is needed.");
        }

        var level = await db.SecurityLevels.AsNoTracking().SingleAsync(l => l.Id == area.SecurityLevelId, ct);
        var mapped = await db.AccessLevelMappings.AnyAsync(m => m.AreaId == areaId, ct);
        var existing = await db.Entitlements.AsNoTracking()
            .Where(e => e.PersonId == subjectId && e.AreaId == areaId)
            .Select(e => new { e.Status, e.EffectiveEnd })
            .ToListAsync(ct);

        var decision = AccessRequestPolicy.Evaluate(new AccessRequestInput(
            subject.Status,
            subject.Eligibility,
            level.MaxDurationDays,
            mapped,
            existing.Select(e => (e.Status, e.EffectiveEnd)).ToList(),
            request.RequestedStart,
            request.RequestedEnd,
            request.Justification,
            now));
        if (decision.IsFailure)
        {
            return decision.Error;
        }

        var steps = ApproverResolver.Resolve(
            ApprovalRouter.BuildSteps(level.ApproverChain),
            new PersonSnapshot(subject.Id, subject.ManagerId, subject.SponsorId),
            me,
            requester.ManagerId,
            area.Owners.Select(o => o.PersonId).ToHashSet(),
            await db.RoleAssignments.AsNoTracking().Where(r => r.Role == AssignedRole.TenantAdmin)
                .OrderBy(r => r.PersonId).Select(r => r.PersonId).Distinct().ToListAsync(ct));
        if (steps.IsFailure)
        {
            return steps.Error;
        }

        var entitlement = Entitlement.Create(
            subject.Id,
            area.Id,
            me,
            request.RequestedStart,
            request.RequestedEnd,
            decision.Value.EffectiveEnd,
            request.Justification.Trim(),
            steps.Value,
            now);
        db.Entitlements.Add(entitlement);
        audit.Record(
            AuditActor.ForPerson(me),
            "entitlement.requested",
            AuditSubjects.Entitlement,
            entitlement.Id.Value.ToString(),
            new
            {
                personId = subject.Id.Value,
                areaId = area.Id.Value,
                requestedStart = request.RequestedStart.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                requestedEnd = request.RequestedEnd.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                effectiveEnd = entitlement.EffectiveEnd.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                cappedToEligibility = entitlement.CappedToEligibility,
            });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // A concurrent request for the same person and area won the unique index (GH-ACCESS-03).
            if (await db.Entitlements.AsNoTracking().AnyAsync(
                e =>
                    e.PersonId == subjectId && e.AreaId == areaId && e.Status == EntitlementStatus.PendingApproval
                        && e.Id != entitlement.Id, ct))
            {
                return Error.Conflict("request.duplicate", "An open request or active access already exists for this area.");
            }

            throw;
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Access requested {EntitlementId} for area {AreaId}", entitlement.Id.Value, area.Id.Value);
        }

        return (await dtos.BuildAsync([entitlement], ct))[0];
    }
}
