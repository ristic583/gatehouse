using Gatehouse.Connector.Contracts.Events;

namespace Gatehouse.Connector.Contracts.Adapters;

public sealed record PacsDoorEvent(
    Guid EventId, DateTimeOffset OccurredAt, DoorEventType Type, string PacsDoorRef, string? PacsReaderRef, string? CardNumber,
    string? PersonRef, ReaderDirection? Direction);
