using Gatehouse.Infrastructure.Messaging.Outbox;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.IntegrationTests.Persistence;

public sealed class OutboxInterceptorTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Saving_aggregate_writes_events_to_outbox_in_same_transaction()
    {
        var ct = TestContext.Current.CancellationToken;
        var testDb = await TenantTestDb.CreateAsync(postgres, ct);
        Guid entitlementId;
        await using (var db = testDb.NewContext(new DomainEventOutboxInterceptor()))
        {
            var (entitlement, _, _, _) = AccessData.Seed(db);
            entitlementId = entitlement.Id.Value;
            await db.SaveChangesAsync(ct);
            Assert.Empty(entitlement.DomainEvents);
        }

        await using (var db = testDb.NewContext())
        {
            var types = await db.OutboxMessages.Select(m => m.Type).ToListAsync(ct);
            Assert.Contains("AccessRequested", types);
            Assert.Contains("ApprovalStepActivated", types);
            Assert.Equal(3, types.Count(t => t == "PersonCreated"));
            var requested = await db.OutboxMessages.SingleAsync(m => m.Type == "AccessRequested", ct);
            Assert.Contains(entitlementId.ToString(), requested.Payload, StringComparison.Ordinal);
            Assert.Null(requested.DispatchedAt);
        }
    }

    [Fact]
    public async Task Rolled_back_transaction_writes_no_outbox_rows()
    {
        var ct = TestContext.Current.CancellationToken;
        var testDb = await TenantTestDb.CreateAsync(postgres, ct);
        await using (var db = testDb.NewContext(new DomainEventOutboxInterceptor()))
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            AccessData.Seed(db);
            await db.SaveChangesAsync(ct);
            await tx.RollbackAsync(ct);
        }

        await using (var db = testDb.NewContext())
        {
            Assert.Equal(0, await db.OutboxMessages.CountAsync(ct));
            Assert.Equal(0, await db.Entitlements.CountAsync(ct));
        }
    }
}
