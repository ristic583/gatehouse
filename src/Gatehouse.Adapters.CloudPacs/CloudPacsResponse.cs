using System.Text.Json;
using Gatehouse.Connector.Contracts.Adapters;

namespace Gatehouse.Adapters.CloudPacs;

/// <summary>Raw response body, or the mapped failure.</summary>
internal sealed record CloudPacsResponse(string? Body, AdapterResult? Failure)
{
    public string? Property(string name)
    {
        if (string.IsNullOrWhiteSpace(Body))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(Body);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty(name, out var value)
                ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
