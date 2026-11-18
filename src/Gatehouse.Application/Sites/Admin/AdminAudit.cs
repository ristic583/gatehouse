using Gatehouse.Application.Audit;
using Gatehouse.Application.Identity;
using Gatehouse.Domain.Audit;

namespace Gatehouse.Application.Sites.Admin;

/// <summary>Records a tenant-admin change with the signed-in person as actor (caller saves).</summary>
public sealed class AdminAudit(ICurrentUser user, IAuditLog audit)
{
    public async Task RecordAsync(string action, string subjectType, Guid subjectId, object? payload, CancellationToken ct) =>
        audit.Record(AuditActor.ForPerson(await user.RequirePersonIdAsync(ct)), action, subjectType, subjectId.ToString(), payload);
}
