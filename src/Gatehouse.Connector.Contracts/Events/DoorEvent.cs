namespace Gatehouse.Connector.Contracts.Events;

public sealed record DoorEvent(
    Guid EventId, DateTimeOffset OccurredAt, DoorEventType Type, string PacsDoorRef, string? PacsReaderRef, string? CardNumber,
    string? PersonRef, ReaderDirection? Direction);
