namespace Gatehouse.Domain.Common;

public interface IDomainEvent
{
    public Guid EventId { get; }

    public DateTimeOffset OccurredAt { get; }
}
