using Gatehouse.Connector.Contracts.Events;

namespace Gatehouse.Application.Connectors.Messages;

/// <summary>A door event as published by the gateway: the card number is already hashed (SHA-256 lowercase hex).</summary>
public sealed record NormalizedDoorEvent(
    Guid EventId,
    DateTimeOffset OccurredAt,
    DoorEventType Type,
    string PacsDoorRef,
    string? PacsReaderRef,
    string? CardNumberHash,
    string? PersonRef,
    ReaderDirection? Direction);
