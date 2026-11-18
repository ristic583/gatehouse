using Gatehouse.Agent.Storage;
using Gatehouse.Connector.Contracts.Adapters;
using Gatehouse.Connector.Contracts.Events;

namespace Gatehouse.UnitTests.Agent;

public sealed class PendingEventStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gh-agent-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    internal static PacsDoorEvent Event(DateTimeOffset at, string door = "D-1") =>
        new(Guid.NewGuid(), at, DoorEventType.AccessGranted, door, "R-1", "1234567890", "P-1", ReaderDirection.In);

    [Fact]
    public async Task Add_is_idempotent_and_take_is_ordered()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new PendingEventStore(new AgentDatabase(_dir));
        var t0 = new DateTimeOffset(2026, 11, 10, 8, 0, 0, TimeSpan.Zero);
        var late = Event(t0.AddMinutes(2));
        var early = Event(t0.AddHours(1).ToOffset(TimeSpan.FromHours(2)).AddHours(-1)); // t0 expressed in +02:00
        var middle = Event(t0.AddMinutes(1));

        await store.AddAsync(late, ct);
        await store.AddAsync(early, ct);
        await store.AddAsync(middle, ct);
        await store.AddAsync(late, ct);

        Assert.Equal(3, await store.CountAsync(ct));
        var taken = await store.TakeAsync(10, ct);
        Assert.Equal([early.EventId, middle.EventId, late.EventId], taken.Select(e => e.EventId));
        Assert.Equal("1234567890", taken[0].CardNumber);
        Assert.Equal([early.EventId], (await store.TakeAsync(1, ct)).Select(e => e.EventId));

        await store.RemoveAsync([early.EventId, middle.EventId], ct);
        Assert.Equal([late.EventId], (await store.TakeAsync(10, ct)).Select(e => e.EventId));
    }
}
