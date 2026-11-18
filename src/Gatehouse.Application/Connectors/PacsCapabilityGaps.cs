using System.Text.Json;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Adapters;

namespace Gatehouse.Application.Connectors;

/// <summary>Capability gap codes from a stored <c>PacsInstance.CapabilitiesJson</c> (adapter-interface.md §6).</summary>
public static class PacsCapabilityGaps
{
    public const string NativeValidity = "NativeValidity";
    public const string PinWrite = "PinWrite";
    public const string PhotoWrite = "PhotoWrite";
    public const string RecordExport = "RecordExport";
    public const string MomentaryUnlock = "MomentaryUnlock";
    public const string Lockdown = "Lockdown";

    /// <summary>Gap codes in contract order; <c>null</c> when the capabilities are unknown (no heartbeat yet or unreadable JSON).</summary>
    public static IReadOnlyList<string>? From(string? capabilitiesJson)
    {
        if (Parse(capabilitiesJson) is not { } c)
        {
            return null;
        }

        var gaps = new List<string>();
        if (!c.NativeValidity)
        {
            gaps.Add(NativeValidity);
        }

        if (!c.PinWrite)
        {
            gaps.Add(PinWrite);
        }

        if (!c.PhotoWrite)
        {
            gaps.Add(PhotoWrite);
        }

        if (!c.RecordExport)
        {
            gaps.Add(RecordExport);
        }

        if (!Has(c, DoorCommandKind.MomentaryUnlock))
        {
            gaps.Add(MomentaryUnlock);
        }

        if (!Has(c, DoorCommandKind.Lockdown))
        {
            gaps.Add(Lockdown);
        }

        return gaps;
    }

    /// <summary>Whether the instance supports the door command kind; unknown capabilities (<c>null</c>) count as supported.</summary>
    public static bool Supports(string? capabilitiesJson, DoorCommandKind kind) =>
        capabilitiesJson is null || Parse(capabilitiesJson) is not { } c || Has(c, kind);

    private static bool Has(PacsCapabilities c, DoorCommandKind kind) => c.DoorCommands?.Contains(kind) == true;

    private static PacsCapabilities? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PacsCapabilities>(json, ConnectorJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
