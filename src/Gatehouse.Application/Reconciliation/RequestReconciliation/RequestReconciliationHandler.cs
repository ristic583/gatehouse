using System.Security.Claims;
using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Connectors;
using Gatehouse.Application.Identity;
using Gatehouse.Application.People.Dtos;
using Gatehouse.Application.Reconciliation.Dtos;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Reconciliation.RequestReconciliation;

/// <summary>Security staff start a reconciliation of a PACS instance (authorization.md §10, domain-model.md §2.7).</summary>
public sealed class RequestReconciliationHandler(
    ITenantDb db,
    ICurrentUser currentUser,
    IAuthorizationService authorization,
    ReconciliationStarter starter,
    IConnectorCommandSignal signal,
    ITenantContext tenant)
{
    public async Task<Result<ReconciliationReportSummaryDto>> HandleAsync(RequestReconciliationCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var instance = await db.PacsInstances.FirstOrDefaultAsync(p => p.Id == command.PacsInstanceId, ct);
        var resource = instance is null ? null : ReconciliationScope.ResourceFor(instance);
        if (instance is null
            || !(await authorization.AuthorizeAsync(new ClaimsPrincipal(), resource, new ViewReconciliationRequirement())).Succeeded)
        {
            return Error.NotFound("reconciliation.instance_not_found", "PACS instance not found.");
        }

        if (!(await authorization.AuthorizeAsync(new ClaimsPrincipal(), resource, new RunReconciliationRequirement())).Succeeded)
        {
            return Error.Forbidden("auth.forbidden", "You are not allowed to run a reconciliation of this PACS instance.");
        }

        var me = await currentUser.RequirePersonIdAsync(ct);
        var started = await starter.StartAsync(instance, me, AuditActor.ForPerson(me), ct);
        if (started.IsFailure)
        {
            return started.Error;
        }

        await db.SaveChangesAsync(ct);
        await signal.NotifyAsync(tenant.TenantId, instance.Id, ct);

        var report = started.Value;
        var requester = await db.Persons.AsNoTracking().Where(p => p.Id == me)
            .Select(p => new PersonRefDto(p.Id.Value, p.GivenName + " " + p.FamilyName)).FirstOrDefaultAsync(ct);
        return new ReconciliationReportSummaryDto(
            report.Id.Value, instance.Id.Value, instance.Name, report.Status.ToString(), report.RequestedAt,
            requester, report.CapturedAt, report.ComparedAt, null, null, null);
    }
}
