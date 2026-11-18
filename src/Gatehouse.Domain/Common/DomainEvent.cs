namespace Gatehouse.Domain.Common;

public abstract record DomainEvent(DateTimeOffset OccurredAt) : IDomainEvent
{
    public Guid EventId { get; init; } = Guid.CreateVersion7();
}
