using Gatehouse.Infrastructure.LiveBuilding;
using Gatehouse.IntegrationTests.Infrastructure;
using Gatehouse.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.IntegrationTests.LiveBuilding;

public sealed class DoorEventPartitionsTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Ensure_is_idempotent_and_drop_removes_old_days()
    {
        var ct = TestContext.Current.CancellationToken;
        var testDb = await TenantTestDb.CreateAsync(postgres, ct);
        await using var db = testDb.NewContext();
        var today = new DateOnly(2026, 11, 9);
        await DoorEventPartitions.EnsureAsync(db, today.AddDays(-3), 5, ct);
        await DoorEventPartitions.EnsureAsync(db, today.AddDays(-3), 5, ct);
        Assert.Equal(5, (await PartitionsAsync(db, ct)).Count);

        Assert.Equal(2, await DoorEventPartitions.DropOlderThanAsync(db, today.AddDays(-1), ct));
        var left = await PartitionsAsync(db, ct);
        Assert.Equal(["door_events_20261108", "door_events_20261109", "door_events_20261110"], left);
        Assert.Equal(0, await DoorEventPartitions.CountDefaultRowsAsync(db, ct));
    }

    private static async Task<List<string>> PartitionsAsync(DbContext db, CancellationToken ct) =>
        (await db.Database.SqlQueryRaw<string>(
            """
            SELECT c.relname AS "Value" FROM pg_inherits i JOIN pg_class c ON c.oid = i.inhrelid
            JOIN pg_class p ON p.oid = i.inhparent WHERE p.relname = 'door_events' AND c.relname <> 'door_events_default'
            """).ToListAsync(ct)).Order(StringComparer.Ordinal).ToList();
}
