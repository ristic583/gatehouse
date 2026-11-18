using System.Text.Json;

namespace Gatehouse.Domain.Audit;

public static class AuditCanonicalJson
{
    public static string Canonicalize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return "{}";
        }

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("The audit payload must be a JSON object.", nameof(json));
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            Write(writer, doc.RootElement);
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void Write(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var p in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(p.Name);
                    Write(writer, p.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    Write(writer, item);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                if (!element.TryGetInt64(out var n))
                {
                    throw new ArgumentException("Audit payloads allow integer numbers only.");
                }

                writer.WriteNumberValue(n);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            default:
                writer.WriteNullValue();
                break;
        }
    }
}
