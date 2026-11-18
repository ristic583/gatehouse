using Gatehouse.Application.Audit.VerifyAuditChain;
using Gatehouse.Domain.Audit;
using Gatehouse.IntegrationTests.Infrastructure;
using Gatehouse.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.IntegrationTests.Audit;

public sealed class AuditChainVerificationTests(PostgresFixture postgres)
{
    private static async Task<TenantTestDb> SeedAsync(PostgresFixture postgres, CancellationToken ct)
    {
        var testDb = await TenantTestDb.CreateAsync(postgres, ct);
        await using var db = testDb.NewContext();
        for (var i = 0; i < 4; i++)
        {
            db.AuditEntries.Add(AuditEntry.CreatePending(AuditActor.ForSystem("test"), "entitlement.requested", "Entitlement", "e" + i, "{}", DateTimeOffset.UtcNow));
        }

        await db.SaveChangesAsync(ct);
        return testDb;
    }

    private static async Task<Gatehouse.Application.Audit.Dtos.AuditVerificationDto> VerifyAsync(TenantTestDb testDb, CancellationToken ct)
    {
        await using var db = testDb.NewContext();
        var result = await new VerifyAuditChainHandler(db, TimeProvider.System)
            .HandleAsync(new VerifyAuditChainQuery(), ct);
        return result.Value;
    }

    [Fact]
    public async Task Intact_chain_verifies()
    {
        var ct = TestContext.Current.CancellationToken;
        var dto = await VerifyAsync(await SeedAsync(postgres, ct), ct);
        Assert.True(dto.Verified);
        Assert.Equal(4, dto.EntryCount);
        Assert.Equal(4, dto.LastSequence);
    }

    [Fact]
    public async Task Tampered_row_breaks_verification()
    {
        var ct = TestContext.Current.CancellationToken;
        var testDb = await SeedAsync(postgres, ct);
        await BypassAsync(testDb, "UPDATE audit_entries SET action = 'x.tampered' WHERE sequence = 2", ct);
        var dto = await VerifyAsync(testDb, ct);
        Assert.False(dto.Verified);
        Assert.Equal(2, dto.FirstInvalidSequence);
    }

    [Fact]
    public async Task Deleted_row_breaks_verification()
    {
        var ct = TestContext.Current.CancellationToken;
        var testDb = await SeedAsync(postgres, ct);
        await BypassAsync(testDb, "DELETE FROM audit_entries WHERE sequence = 2", ct);
        var dto = await VerifyAsync(testDb, ct);
        Assert.False(dto.Verified);
        Assert.Equal(3, dto.FirstInvalidSequence);
    }

    private static async Task BypassAsync(TenantTestDb testDb, string sql, CancellationToken ct)
    {
        await using var db = testDb.NewContext();
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit_entries DISABLE TRIGGER audit_entries_no_update_delete", ct);
        await db.Database.ExecuteSqlRawAsync(sql, ct);
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE audit_entries ENABLE TRIGGER audit_entries_no_update_delete", ct);
    }
}
