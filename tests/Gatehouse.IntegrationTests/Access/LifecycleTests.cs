using Gatehouse.Application.People.Lifecycle;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Audit;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Access;

/// <summary>M11-17 (GH-ACCESS-02): a request still pending when its window ends gets a terminal state and leaves the inboxes.</summary>
public sealed class LifecycleTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatehouseApiFactory _factory = new(postgres);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => _factory.InitializeAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Ended_pending_request_expires_and_leaves_inbox()
    {
        using var anna = _factory.CreateClientFor(DemoUser.Anna);
        using var mikkel = _factory.CreateClientFor(DemoUser.Mikkel);
        using var created = await AccessRequestEndpointTests.PostRequestAsync(_factory, anna, "nordlys", "CPH-SERVER");
        var request = await AccessRequestEndpointTests.CreatedAsync(created);
        Assert.Contains(await ApprovalEndpointTests.InboxAsync(mikkel), i => i.EntitlementId == request.EntitlementId);

        var id = new EntitlementId(request.EntitlementId);
        await using (var scope = await _factory.CreateTenantScopeAsync("nordlys"))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var entitlement = await db.Entitlements.SingleAsync(e => e.Id == id, Ct);
            db.Entry(entitlement).Property(e => e.EffectiveEnd).CurrentValue = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync(Ct);
        }

        // Ended but not yet expired by the run: no longer offered for a decision.
        Assert.DoesNotContain(await ApprovalEndpointTests.InboxAsync(mikkel), i => i.EntitlementId == request.EntitlementId);

        await using (var scope = await _factory.CreateTenantScopeAsync("nordlys"))
        {
            await scope.ServiceProvider.GetRequiredService<LifecycleRunner>().RunTenantAsync(DateTimeOffset.UtcNow, Ct);
        }

        await using var check = await _factory.CreateTenantScopeAsync("nordlys");
        var checkDb = check.ServiceProvider.GetRequiredService<TenantDbContext>();
        var expired = await checkDb.Entitlements.AsNoTracking().SingleAsync(e => e.Id == id, Ct);
        Assert.Equal(EntitlementStatus.Expired, expired.Status);
        Assert.NotNull(expired.ClosedAt);
        Assert.DoesNotContain(expired.Steps, s => s.Status is ApprovalStepStatus.Pending or ApprovalStepStatus.Waiting);
        var audit = await checkDb.AuditEntries.AsNoTracking().SingleAsync(a => a.Action == "request.expired_unapproved", Ct);
        Assert.Equal((request.EntitlementId.ToString(), AuditActorKind.System, "expiry"), (audit.SubjectId, audit.ActorKind, audit.ActorId));
        Assert.DoesNotContain(await ApprovalEndpointTests.InboxAsync(mikkel), i => i.EntitlementId == request.EntitlementId);
    }
}
