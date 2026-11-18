using System.Text.Json;
using Gatehouse.Connector.Contracts.Adapters;

namespace Gatehouse.Adapters.CloudPacs;

/// <summary>Maps CloudPACS outcomes to <see cref="AdapterResult"/> (adapter-interface.md §5).</summary>
internal static class CloudPacsErrorMapper
{
    public static AdapterResult Timeout() => AdapterResult.Failed("cloudpacs.timeout", "CloudPACS request timed out", true);

    public static AdapterResult Unreachable(string reason) => AdapterResult.Failed("cloudpacs.unreachable", reason, true);

    public static AdapterResult FromResponse(int status, string? body)
    {
        var vendorCode = VendorCode(body);
        return status switch
        {
            429 => AdapterResult.Failed("cloudpacs.rate_limited", "CloudPACS rate limit (429)", true),
            >= 500 => AdapterResult.Failed("cloudpacs.server_error", $"CloudPACS server error ({status})", true),
            404 when vendorCode == "unknown_access_group" =>
                AdapterResult.Failed("cloudpacs.unknown_access_level", "Unknown CloudPACS access group", false),
            404 when vendorCode == "unknown_cardholder" =>
                AdapterResult.Failed("cloudpacs.unknown_cardholder", "Unknown CloudPACS cardholder", false),
            404 when vendorCode == "unknown_door" => AdapterResult.Failed("cloudpacs.unknown_door", "Unknown CloudPACS door", false),
            _ => AdapterResult.Failed("cloudpacs.rejected", $"CloudPACS rejected the request ({status} {vendorCode})", false),
        };
    }

    private static string? VendorCode(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("error", out var e)
                ? e.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
