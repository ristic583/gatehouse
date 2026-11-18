using System.Globalization;
using Gatehouse.Application.Common.Messaging;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.People;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gatehouse.Application.Access.Notifications;

/// <summary>
/// Consumer logic for <c>ApprovalStepActivated</c> (lab finding #10): emails the step's approver (the assigned person, or every area
/// owner other than the subject and requester). No-op when email is off or the step is no longer pending.
/// </summary>
public sealed partial class NotifyApproversHandler(
    ITenantDb db,
    IEmailSender email,
    IOptions<NotificationOptions> options,
    ILogger<NotifyApproversHandler> logger)
{
    public async Task<int> HandleAsync(NotifyApproversCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!email.IsEnabled)
        {
            return 0;
        }

        var entitlement = await db.Entitlements.AsNoTracking().FirstOrDefaultAsync(e => e.Id == command.EntitlementId, ct);
        var step = entitlement?.Steps.FirstOrDefault(s => s.Id == command.StepId);
        if (entitlement is null || step is null || step.Status != ApprovalStepStatus.Pending
            || entitlement.Status != EntitlementStatus.PendingApproval)
        {
            return 0;
        }

        var area = await db.Areas.AsNoTracking().SingleAsync(a => a.Id == entitlement.AreaId, ct);
        var approverIds = command.AssignedApproverId is { } assigned
            ? [assigned]
            : area.Owners.Select(o => o.PersonId).Where(p => p != entitlement.PersonId && p != entitlement.RequestedById).ToList();
        var ids = approverIds.Append(entitlement.PersonId).Append(entitlement.RequestedById).Distinct().ToList();
        var people = await db.Persons.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var subject = people[entitlement.PersonId];
        var requester = people[entitlement.RequestedById];

        var sent = 0;
        foreach (var approverId in approverIds)
        {
            if (!people.TryGetValue(approverId, out var approver) || approver.Status != PersonStatus.Active
                || string.IsNullOrWhiteSpace(approver.Email))
            {
                continue;
            }

            await email.SendAsync(
                approver.Email, $"Access request to approve: {area.Name}", Body(approver, subject, requester, area.Name, entitlement), ct);
            sent++;
        }

        LogNotified(sent, entitlement.Id.Value);
        return sent;
    }

    private string Body(Person approver, Person subject, Person requester, string areaName, Entitlement entitlement)
    {
        var who = subject.Id == requester.Id
            ? $"{subject.GivenName} {subject.FamilyName}"
            : $"{requester.GivenName} {requester.FamilyName} on behalf of {subject.GivenName} {subject.FamilyName}";
        var link = options.Value.PublicUrl is { Length: > 0 } url ? $"{url.TrimEnd('/')}/approvals" : "Approvals in Gatehouse";
        return string.Create(CultureInfo.InvariantCulture, $"""
            Hello {approver.GivenName},

            {who} requests access to {areaName}
            from {entitlement.RequestedStart:yyyy-MM-dd HH:mm} to {entitlement.EffectiveEnd:yyyy-MM-dd HH:mm} (UTC).

            Reason: {entitlement.Justification}

            Review it here: {link}

            This message was sent by Gatehouse. You get it because you approve access to {areaName}.
            """);
    }

    [LoggerMessage(
        Level = LogLevel.Information, Message = "Approval notification sent to {Count} approver(s) for entitlement {EntitlementId}")]
    private partial void LogNotified(int count, Guid entitlementId);
}
