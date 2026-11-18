using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gatehouse.Application.Platform;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Platform;
using Gatehouse.Infrastructure.Persistence.Catalog;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Infrastructure.Platform;

public sealed class PlatformAuditLog(CatalogDbContext db, TimeProvider time) : IPlatformAuditLog
{
    public async Task WriteAsync(PlatformActor actor, string action, TenantId? tenantId, object? data, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var pending = db.PlatformAuditEntries.Local.OrderByDescending(e => e.Sequence).FirstOrDefault();
        var last = pending ?? await db.PlatformAuditEntries.AsNoTracking()
            .OrderByDescending(e => e.Sequence)
            .FirstOrDefaultAsync(ct);

        var sequence = (last?.Sequence ?? 0) + 1;
        var previous = last?.Hash ?? AuditHasher.GenesisHash;
        // Postgres timestamptz keeps microseconds; truncate before hashing so the chain verifies after a round trip.
        var utc = time.GetUtcNow();
        var now = new DateTimeOffset(utc.UtcTicks - (utc.UtcTicks % 10), TimeSpan.Zero);
        var canonical = AuditCanonicalJson.Canonicalize(data is null ? null : JsonSerializer.Serialize(data));
        var hash = Compute(previous, sequence, now, actor, action, tenantId, canonical);
        db.PlatformAuditEntries.Add(PlatformAuditEntry.Create(sequence, now, actor, action, tenantId, canonical, previous, hash));
    }

    public static string Compute(
        string previousHash,
        long sequence,
        DateTimeOffset occurredAt,
        PlatformActor actor,
        string action,
        TenantId? tenantId,
        string canonicalData)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("sequence", sequence);
            writer.WriteString(
                "occurredAt", occurredAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture));
            writer.WriteString("actorKind", actor.Kind.ToString());
            writer.WriteString("actorId", actor.Id);
            writer.WriteString("action", action);
            writer.WriteString("subjectType", "tenant");
            writer.WriteString("subjectId", tenantId?.Value ?? string.Empty);
            writer.WritePropertyName("payload");
            writer.WriteRawValue(canonicalData);
            writer.WriteEndObject();
        }

        return Convert.ToHexStringLower(
            SHA256.HashData(Encoding.UTF8.GetBytes(previousHash + "\n" + Encoding.UTF8.GetString(stream.ToArray()))));
    }
}
