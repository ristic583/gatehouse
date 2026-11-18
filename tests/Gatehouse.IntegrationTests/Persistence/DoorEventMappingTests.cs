using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.LiveBuilding;
using Gatehouse.Domain.Sites;
using Gatehouse.Infrastructure.LiveBuilding;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.IntegrationTests.Persistence;

public sealed class DoorEventMappingTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset At = new(2026, 11, 9, 8, 42, 13, TimeSpan.Zero);

    private static DoorEvent NewEvent(Guid id) => DoorEvent.Create(
        id, At, At.AddMilliseconds(300), PacsInstanceId.New(), DoorId.New(), ReaderId.New(), "D-2.04", "R-2.04",
        DoorEventType.AccessGranted, null, new string('a', 64), ReaderDirection.In);

    [Fact]
    public async Task Round_trips_into_daily_partition()
    {
        var ct = TestContext.Current.CancellationToken;
        var testDb = await TenantTestDb.CreateAsync(postgres, ct);
        await using var db = testDb.NewContext();
        await DoorEventPartitions.EnsureAsync(db, DateOnly.FromDateTime(At.UtcDateTime), 1, ct);
        var evt = NewEvent(Guid.CreateVersion7());
        db.DoorEvents.Add(evt);
        await db.SaveChangesAsync(ct);

        await using var read = testDb.NewContext();
        var stored = await read.DoorEvents.AsNoTracking().SingleAsync(e => e.Id == evt.Id, ct);
        Assert.Equal(DoorEventType.AccessGranted, stored.Type);
        Assert.Equal(ReaderDirection.In, stored.Direction);
        Assert.Equal(evt.DoorId, stored.DoorId);
        var partition = await read.Database
            .SqlQueryRaw<string>("SELECT tableoid::regclass::text AS \"Value\" FROM door_events WHERE id = {0}", evt.Id)
            .SingleAsync(ct);
        Assert.Equal("door_events_20261109", partition);
    }

    [Fact]
    public async Task Duplicate_event_id_same_time_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        var testDb = await TenantTestDb.CreateAsync(postgres, ct);
        var id = Guid.CreateVersion7();
        await using (var db = testDb.NewContext())
        {
            db.DoorEvents.Add(NewEvent(id));
            await db.SaveChangesAsync(ct);
        }

        await using var again = testDb.NewContext();
        again.DoorEvents.Add(NewEvent(id));
        await Assert.ThrowsAsync<DbUpdateException>(() => again.SaveChangesAsync(ct));
    }
}
