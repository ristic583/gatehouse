using Gatehouse.Domain.Common;

namespace Gatehouse.UnitTests.Common;

public sealed class AggregateRootTests
{
    [Fact]
    public void Raised_events_are_collected_and_cleared()
    {
        var aggregate = new TestAggregate(Guid.CreateVersion7());
        var now = new DateTimeOffset(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);

        aggregate.DoSomething(now);
        aggregate.DoSomething(now);

        Assert.Equal(2, aggregate.DomainEvents.Count);
        Assert.All(aggregate.DomainEvents, e => Assert.Equal(now, e.OccurredAt));
        Assert.NotEqual(aggregate.DomainEvents[0].EventId, aggregate.DomainEvents[1].EventId);

        aggregate.ClearDomainEvents();

        Assert.Empty(aggregate.DomainEvents);
    }

    private sealed class TestAggregate(Guid id) : AggregateRoot<Guid>(id)
    {
        public void DoSomething(DateTimeOffset now) => Raise(new SomethingHappened(now));
    }

    private sealed record SomethingHappened(DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
}
