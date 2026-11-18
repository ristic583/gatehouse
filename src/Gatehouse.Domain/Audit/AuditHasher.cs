using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Gatehouse.Domain.Audit;

public static class AuditHasher
{
    public const string GenesisHash = "0000000000000000000000000000000000000000000000000000000000000000";

    public static string Compute(
        string prevHash,
        long sequence,
        DateTimeOffset occurredAt,
        AuditActorKind actorKind,
        string actorId,
        string action,
        string subjectType,
        string subjectId,
        string payloadJson)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("sequence", sequence);
            writer.WriteString(
                "occurredAt", occurredAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture));
            writer.WriteString("actorKind", actorKind.ToString());
            writer.WriteString("actorId", actorId);
            writer.WriteString("action", action);
            writer.WriteString("subjectType", subjectType);
            writer.WriteString("subjectId", subjectId);
            writer.WritePropertyName("payload");
            writer.WriteRawValue(AuditCanonicalJson.Canonicalize(payloadJson));
            writer.WriteEndObject();
        }

        var canonical = Encoding.UTF8.GetString(stream.ToArray());
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(prevHash + "\n" + canonical)));
    }
}
