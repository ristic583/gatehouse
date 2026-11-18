using Gatehouse.Domain.Access;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.IntegrationTests.Persistence;

public sealed class AccessMappingTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Entitlement_with_steps_round_trips()
    {
        var ct = TestContext.Current.CancellationToken;
        var testDb = await TenantTestDb.CreateAsync(postgres, ct);
        EntitlementId id;
        await using (var db = testDb.NewContext())
        {
            var (entitlement, manager, _, _) = AccessData.Seed(db);
            entitlement.Approve(entitlement.Steps[0].Id, manager.Id, "fine", MappingData.Now);
            await db.SaveChangesAsync(ct);
            id = entitlement.Id;
        }

        await using (var db = testDb.NewContext())
        {
            var loaded = await db.Entitlements.SingleAsync(e => e.Id == id, ct);
            Assert.Equal(2, loaded.Steps.Count);
            Assert.Equal(ApprovalStepStatus.Approved, loaded.Steps[0].Status);
            Assert.Equal("fine", loaded.Steps[0].Comment);
            Assert.Equal(ApprovalStepStatus.Pending, loaded.Steps[1].Status);
            Assert.Equal(ApproverKind.AreaOwner, loaded.Steps[1].ApproverKind);
            Assert.Null(loaded.Revocation);
        }
    }

    [Fact]
    public async Task Revocation_round_trips_without_touching_grant_fields()
    {
        var ct = TestContext.Current.CancellationToken;
        var testDb = await TenantTestDb.CreateAsync(postgres, ct);
        EntitlementId id;
        DateTimeOffset? approvedAt;
        await using (var db = testDb.NewContext())
        {
            var (entitlement, manager, owner, _) = AccessData.Seed(db);
            entitlement.Approve(entitlement.Steps[0].Id, manager.Id, null, MappingData.Now);
            entitlement.Approve(entitlement.Steps[1].Id, owner.Id, null, MappingData.Now.AddMinutes(1));
            await db.SaveChangesAsync(ct);
            id = entitlement.Id;
            approvedAt = entitlement.ApprovedAt;
        }

        await using (var db = testDb.NewContext())
        {
            var loaded = await db.Entitlements.SingleAsync(e => e.Id == id, ct);
            Assert.True(loaded.Revoke(null, "Left the company", RevocationKind.Termination, MappingData.Now.AddDays(2)).IsSuccess);
            await db.SaveChangesAsync(ct);
        }

        await using (var db = testDb.NewContext())
        {
            var loaded = await db.Entitlements.AsNoTracking().SingleAsync(e => e.Id == id, ct);
            Assert.Equal(EntitlementStatus.Revoked, loaded.Status);
            Assert.Equal(approvedAt, loaded.ApprovedAt);
            Assert.Equal(MappingData.Now.AddDays(30), loaded.EffectiveEnd);
            Assert.Equal(new Revocation(null, MappingData.Now.AddDays(2), "Left the company", RevocationKind.Termination), loaded.Revocation);
        }
    }

    [Fact]
    public async Task Concurrent_update_throws_concurrency_exception()
    {
        var ct = TestContext.Current.CancellationToken;
        var testDb = await TenantTestDb.CreateAsync(postgres, ct);
        EntitlementId id;
        Gatehouse.Domain.People.PersonId managerId;
        await using (var db = testDb.NewContext())
        {
            var (entitlement, manager, _, _) = AccessData.Seed(db);
            await db.SaveChangesAsync(ct);
            id = entitlement.Id;
            managerId = manager.Id;
        }

        await using var first = testDb.NewContext();
        await using var second = testDb.NewContext();
        var a = await first.Entitlements.SingleAsync(e => e.Id == id, ct);
        var b = await second.Entitlements.SingleAsync(e => e.Id == id, ct);

        a.Reject(a.Steps[0].Id, managerId, "no", MappingData.Now);
        await first.SaveChangesAsync(ct);

        b.Reject(b.Steps[0].Id, managerId, "also no", MappingData.Now);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(ct));
    }
}
