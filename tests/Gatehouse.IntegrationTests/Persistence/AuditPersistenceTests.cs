using Gatehouse.Domain.Audit;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Gatehouse.IntegrationTests.Persistence;

public sealed class AuditPersistenceTests(PostgresFixture postgres)
{
    private static AuditEntry Entry(string payload = "{}") =>
        AuditEntry.CreatePending(
            AuditActor.ForSystem("test"),
            "entitlement.requested",
            "Entitlement",
            Guid.NewGuid().ToString(),
            payload,
            MappingData.Now.AddTicks(1234567));

    [Fact]
    public async Task Pending_entries_are_sealed_in_order()
    {
        var ct = TestContext.Current.CancellationToken;
        var testDb = await TenantTestDb.CreateAsync(postgres, ct);
        await using (var db = testDb.NewContext())
        {
            db.AuditEntries.AddRange(Entry(), Entry(), Entry());
            await db.SaveChangesAsync(ct);
        }

        await using var read = testDb.NewContext();
        var all = await read.AuditEntries.AsNoTracking().OrderBy(a => a.Sequence).ToListAsync(ct);
        Assert.Equal([1L, 2L, 3L], all.Select(a => a.Sequence));
        Assert.Equal(AuditHasher.GenesisHash, all[0].PrevHash);
        Assert.Equal(all[0].Hash, all[1].PrevHash);
        Assert.Equal(all[1].Hash, all[2].PrevHash);
        Assert.All(all, a => Assert.True(a.HasValidHash()));
    }

    [Fact]
    public async Task Entries_from_separate_saves_continue_the_chain()
    {
        var ct = TestContext.Current.CancellationToken;
        var testDb = await TenantTestDb.CreateAsync(postgres, ct);
        for (var i = 0; i < 2; i++)
        {
            await using var db = testDb.NewContext();
            db.AuditEntries.Add(Entry());
            await db.SaveChangesAsync(ct);
        }

        await using var read = testDb.NewContext();
        var all = await read.AuditEntries.AsNoTracking().OrderBy(a => a.Sequence).ToListAsync(ct);
        Assert.Equal([1L, 2L], all.Select(a => a.Sequence));
        Assert.Equal(all[0].Hash, all[1].PrevHash);
    }

    [Fact]
    public async Task Concurrent_writers_produce_a_contiguous_chain()
    {
        var ct = TestContext.Current.CancellationToken;
        var testDb = await TenantTestDb.CreateAsync(postgres, ct);
        await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            await using var db = testDb.NewContext();
            db.AuditEntries.Add(Entry());
            await db.SaveChangesAsync(ct);
        }));

        await using var read = testDb.NewContext();
        var all = await read.AuditEntries.AsNoTracking().OrderBy(a => a.Sequence).ToListAsync(ct);
        Assert.Equal(Enumerable.Range(1, 10).Select(i => (long)i), all.Select(a => a.Sequence));
        for (var i = 0; i < all.Count; i++)
        {
            Assert.True(all[i].HasValidHash());
            Assert.Equal(i == 0 ? AuditHasher.GenesisHash : all[i - 1].Hash, all[i].PrevHash);
        }
    }

    [Fact]
    public async Task Update_and_delete_are_rejected_by_trigger()
    {
        var ct = TestContext.Current.CancellationToken;
        var testDb = await TenantTestDb.CreateAsync(postgres, ct);
        await using var db = testDb.NewContext();
        db.AuditEntries.Add(Entry());
        await db.SaveChangesAsync(ct);

        await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync("UPDATE audit_entries SET action = 'x.y'", ct));
        await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync("DELETE FROM audit_entries", ct));
        await Assert.ThrowsAsync<PostgresException>(() =>
            db.Database.ExecuteSqlRawAsync("TRUNCATE audit_entries", ct));
    }

    [Fact]
    public async Task Rolled_back_business_change_writes_no_audit_entry()
    {
        var ct = TestContext.Current.CancellationToken;
        var testDb = await TenantTestDb.CreateAsync(postgres, ct);
        await using (var db = testDb.NewContext())
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            AccessData.Seed(db);
            db.AuditEntries.Add(Entry());
            await db.SaveChangesAsync(ct);
            await tx.RollbackAsync(ct);
        }

        await using var read = testDb.NewContext();
        Assert.Equal(0, await read.AuditEntries.CountAsync(ct));
        Assert.Equal(0, await read.Entitlements.CountAsync(ct));
    }

    [Fact]
    public async Task Payload_survives_jsonb_round_trip_with_same_hash()
    {
        var ct = TestContext.Current.CancellationToken;
        var testDb = await TenantTestDb.CreateAsync(postgres, ct);
        await using (var db = testDb.NewContext())
        {
            db.AuditEntries.Add(Entry("{\"z\":[3,1],\"a\":{\"k\":\"v\",\"b\":null},\"t\":true,\"n\":-42}"));
            await db.SaveChangesAsync(ct);
        }

        await using var read = testDb.NewContext();
        var loaded = await read.AuditEntries.AsNoTracking().SingleAsync(ct);
        Assert.True(loaded.HasValidHash());
        Assert.Equal(MappingData.Now.AddTicks(1234560), loaded.OccurredAt);
    }
}
