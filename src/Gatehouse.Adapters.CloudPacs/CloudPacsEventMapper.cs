using Gatehouse.Connector.Contracts.Adapters;
using Gatehouse.Connector.Contracts.Events;

namespace Gatehouse.Adapters.CloudPacs;

/// <summary>Vendor event type → <see cref="DoorEventType"/> (adapter-interface.md §5).</summary>
internal static class CloudPacsEventMapper
{
    private static readonly Dictionary<string, DoorEventType> Types = new(StringComparer.Ordinal)
    {
        ["access.granted"] = DoorEventType.AccessGranted,
        ["access.denied"] = DoorEventType.AccessDenied,
        ["door.opened"] = DoorEventType.DoorOpened,
        ["door.closed"] = DoorEventType.DoorClosed,
        ["door.held_open"] = DoorEventType.DoorHeldOpen,
        ["door.forced"] = DoorEventType.DoorForced,
        ["door.unlocked"] = DoorEventType.DoorUnlocked,
        ["door.relocked"] = DoorEventType.DoorRelocked,
    };

    /// <summary>Returns null for unknown vendor types (skipped by the caller).</summary>
    public static PacsDoorEvent? Map(CloudPacsWebhookEvent e)
    {
        if (!Types.TryGetValue(e.Type ?? string.Empty, out var type))
        {
            return null;
        }

        ReaderDirection? direction = e.Direction switch
        {
            "In" or "in" => ReaderDirection.In,
            "Out" or "out" => ReaderDirection.Out,
            _ => null,
        };
        return new PacsDoorEvent(e.Id, e.OccurredAt, type, e.DoorRef, e.ReaderRef, e.CardNumber, e.ExternalRef, direction);
    }
}
