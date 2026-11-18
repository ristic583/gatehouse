using Gatehouse.Application.Audit;
using Gatehouse.Application.Identity;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Fulfillment;

namespace Gatehouse.Application.Fulfillment.PacsAdmin;

/// <summary>Records a tenant-admin PACS change with the signed-in person as actor (caller saves).</summary>
public sealed class PacsAdminAudit(ICurrentUser user, IAuditLog audit)
{
    public Task RecordAsync(string action, PacsInstanceId id, object payload, CancellationToken ct) =>
        RecordAsync(action, "PacsInstance", id.Value, payload, ct);

    public async Task RecordAsync(string action, string subjectType, Guid id, object payload, CancellationToken ct) =>
        audit.Record(AuditActor.ForPerson(await user.RequirePersonIdAsync(ct)), action, subjectType, id.ToString(), payload);
}
