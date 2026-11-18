using System.Text.Json;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Domain.Audit;

namespace Gatehouse.Application.Audit;

public sealed class AuditLog(ITenantDb db, TimeProvider time) : IAuditLog
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public void Record(AuditActor actor, string action, string subjectType, string subjectId, object? payload = null)
    {
        var json = payload is null ? "{}" : JsonSerializer.Serialize(payload, Options);
        db.AuditEntries.Add(AuditEntry.CreatePending(actor, action, subjectType, subjectId, json, time.GetUtcNow()));
    }
}
