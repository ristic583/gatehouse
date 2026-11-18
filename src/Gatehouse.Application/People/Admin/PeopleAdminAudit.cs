using Gatehouse.Application.Audit;
using Gatehouse.Application.Identity;
using Gatehouse.Domain.Audit;

namespace Gatehouse.Application.People.Admin;

/// <summary>Records a tenant-admin people change with the signed-in person as actor (caller saves).</summary>
public sealed class PeopleAdminAudit(ICurrentUser user, IAuditLog audit)
{
    public async Task RecordAsync(string action, string subjectType, string subjectId, object? payload, CancellationToken ct) =>
        audit.Record(AuditActor.ForPerson(await user.RequirePersonIdAsync(ct)), action, subjectType, subjectId, payload);
}
