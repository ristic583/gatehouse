using System.Security.Claims;
using Gatehouse.Application.Audit;
using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Identity;
using Gatehouse.Application.LiveBuilding.Dtos;
using Gatehouse.Application.LiveBuilding.ListAlarms;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.LiveBuilding;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.LiveBuilding.AcknowledgeAlarm;

/// <summary>Acknowledges an open alarm with a comment (authorization.md §9), audits it and pushes an <c>Alarm</c> live update after
/// save.</summary>
public sealed class AcknowledgeAlarmHandler(
    ITenantDb db,
    ICurrentUser currentUser,
    IAuthorizationService authorization,
    IAuditLog audit,
    ILiveUpdatePublisher publisher,
    TimeProvider time)
{
    public const string AuditSubject = "Alarm";

    public async Task<Result<AlarmDto>> HandleAsync(AcknowledgeAlarmCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var alarmId = new AlarmId(command.AlarmId);
        var siteId = await AlarmProjection.Rows(db, db.Alarms.AsNoTracking().Where(a => a.Id == alarmId))
            .Select(r => r.SiteId)
            .ToListAsync(ct);
        if (siteId.Count == 0
            || !(await authorization.AuthorizeAsync(new ClaimsPrincipal(), siteId[0], new ViewLiveSiteRequirement())).Succeeded)
        {
            return Error.NotFound("alarm.not_found", "Alarm not found.");
        }

        if (!(await authorization.AuthorizeAsync(new ClaimsPrincipal(), siteId[0], new OperateDoorsRequirement())).Succeeded)
        {
            return Error.Forbidden("auth.forbidden", "You may not operate doors on this site.");
        }

        var alarm = await db.Alarms.SingleAsync(a => a.Id == alarmId, ct);
        var by = await currentUser.RequirePersonIdAsync(ct);
        var acknowledged = alarm.Acknowledge(by, command.Comment, time.GetUtcNow());
        if (acknowledged.IsFailure)
        {
            return acknowledged.Error;
        }

        audit.Record(AuditActor.ForPerson(by), "alarm.acknowledged", AuditSubject, alarm.Id.Value.ToString(), new
        {
            kind = alarm.Kind.ToString(),
            doorId = alarm.DoorId.Value,
            comment = alarm.Comment,
        });
        await db.SaveChangesAsync(ct);

        var row = await AlarmProjection.Rows(db, db.Alarms.AsNoTracking().Where(a => a.Id == alarmId)).SingleAsync(ct);
        var dto = row.ToDto();
        await publisher.PublishAsync(new LiveUpdateDto(LiveUpdateKind.Alarm, dto.SiteId, Alarm: dto), ct);
        return dto;
    }
}
