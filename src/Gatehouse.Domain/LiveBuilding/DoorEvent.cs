using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;

namespace Gatehouse.Domain.LiveBuilding;

/// <summary>A normalized door event (partitioned table, ADR-006). <see cref="Id"/> is the agent event id.</summary>
public sealed class DoorEvent
{
    private DoorEvent(
        Guid id,
        DateTimeOffset occurredAt,
        DateTimeOffset receivedAt,
        PacsInstanceId pacsInstanceId,
        DoorId? doorId,
        ReaderId? readerId,
        string pacsDoorRef,
        string? pacsReaderRef,
        DoorEventType type,
        PersonId? personId,
        string? cardNumberHash,
        ReaderDirection? direction)
    {
        Id = id;
        OccurredAt = occurredAt;
        ReceivedAt = receivedAt;
        PacsInstanceId = pacsInstanceId;
        DoorId = doorId;
        ReaderId = readerId;
        PacsDoorRef = pacsDoorRef;
        PacsReaderRef = pacsReaderRef;
        Type = type;
        PersonId = personId;
        CardNumberHash = cardNumberHash;
        Direction = direction;
    }

    // EF Core
    private DoorEvent()
    {
        PacsDoorRef = string.Empty;
    }

    public Guid Id { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    public PacsInstanceId PacsInstanceId { get; private set; }

    public DoorId? DoorId { get; private set; }

    public ReaderId? ReaderId { get; private set; }

    public string PacsDoorRef { get; private set; }

    public string? PacsReaderRef { get; private set; }

    public DoorEventType Type { get; private set; }

    public PersonId? PersonId { get; private set; }

    public string? CardNumberHash { get; private set; }

    public ReaderDirection? Direction { get; private set; }

    public static DoorEvent Create(
        Guid id,
        DateTimeOffset occurredAt,
        DateTimeOffset receivedAt,
        PacsInstanceId pacsInstanceId,
        DoorId? doorId,
        ReaderId? readerId,
        string pacsDoorRef,
        string? pacsReaderRef,
        DoorEventType type,
        PersonId? personId,
        string? cardNumberHash,
        ReaderDirection? direction)
    {
        ArgumentNullException.ThrowIfNull(pacsDoorRef);
        return new DoorEvent(
            id, occurredAt, receivedAt, pacsInstanceId, doorId, readerId, pacsDoorRef, pacsReaderRef, type, personId, cardNumberHash,
            direction);
    }
}
