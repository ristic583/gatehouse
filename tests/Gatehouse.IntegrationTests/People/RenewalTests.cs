using Gatehouse.Application.Access.Renewal;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.People;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.People;

/// <summary>Renewal requests for externals (renewal interval 365 days, lead 14 days by default).</summary>
public sealed class RenewalTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatehouseApiFactory _api = new(postgres);
    private LifecycleArrange _arrange = null!;

    public async ValueTask InitializeAsync()
    {
        await _api.InitializeAsync();
        _arrange = new LifecycleArrange(_api);
        await _arrange.InitializeAsync();
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    [Fact]
    public async Task Renewal_opens_request_capped_at_eligibility()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var eligibilityEnd = now.AddDays(25);
        var personId = await _arrange.ExternalAsync(now.AddDays(-20), eligibilityEnd, ct);
        var old = await _arrange.ApprovedAsync(personId, now.AddDays(-10), now.AddDays(10), ct);

        Assert.True(await RunAsync(now, ct) >= 1);

        var renewal = await RenewalOfAsync(old, ct);
        Assert.NotNull(renewal);
        Assert.Equal(EntitlementStatus.PendingApproval, renewal.Status);
        Assert.Equal(personId, renewal.RequestedById);
        Assert.InRange(renewal.RequestedStart.ToUnixTimeMilliseconds() - now.AddDays(10).ToUnixTimeMilliseconds(), -1, 1);
        Assert.InRange(renewal.EffectiveEnd.ToUnixTimeMilliseconds() - eligibilityEnd.ToUnixTimeMilliseconds(), -1, 1);
        Assert.True(await AuditedAsync(renewal.Id, ct));
    }

    [Fact]
    public async Task Renewal_is_not_requested_twice()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var personId = await _arrange.ExternalAsync(now.AddDays(-20), now.AddDays(90), ct);
        var old = await _arrange.ApprovedAsync(personId, now.AddDays(-10), now.AddDays(5), ct);

        await RunAsync(now, ct);
        await RunAsync(now.AddHours(1), ct);

        await using var scope = await _api.CreateTenantScopeAsync(LifecycleArrange.Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        Assert.Equal(1, await db.Entitlements.AsNoTracking().CountAsync(e => e.PersonId == personId && e.Id != old && e.Justification.StartsWith("Renewal of "), ct));
    }

    [Fact]
    public async Task No_renewal_when_eligibility_ends_first()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var end = now.AddDays(8);
        var personId = await _arrange.ExternalAsync(now.AddDays(-20), end, ct);
        var old = await _arrange.ApprovedAsync(personId, now.AddDays(-10), end, ct);

        await RunAsync(now, ct);

        Assert.Null(await RenewalOfAsync(old, ct));
    }

    [Fact]
    public async Task Renewal_request_needs_normal_approval()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var personId = await _arrange.ExternalAsync(now.AddDays(-20), now.AddDays(90), ct);
        var old = await _arrange.ApprovedAsync(personId, now.AddDays(-10), now.AddDays(7), ct);

        await RunAsync(now, ct);

        var renewal = await RenewalOfAsync(old, ct);
        Assert.NotNull(renewal);
        Assert.Equal(EntitlementStatus.PendingApproval, renewal.Status);
        Assert.NotEmpty(renewal.Steps);
        Assert.Equal(ApprovalStepStatus.Pending, renewal.Steps.OrderBy(s => s.Order).First().Status);
        Assert.All(renewal.Steps, s => Assert.Null(s.DecidedAt));
        Assert.DoesNotContain(renewal.Steps, s => s.AssignedApproverId == personId);
        await using var scope = await _api.CreateTenantScopeAsync(LifecycleArrange.Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        Assert.Equal(EntitlementStatus.Approved, (await db.Entitlements.AsNoTracking().SingleAsync(e => e.Id == old, ct)).Status);
    }

    private async Task<int> RunAsync(DateTimeOffset now, CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync(LifecycleArrange.Tenant);
        return await scope.ServiceProvider.GetRequiredService<RenewalScheduler>().RunAsync(LifecycleSettings.Defaults, now, ct);
    }

    private async Task<Entitlement?> RenewalOfAsync(EntitlementId old, CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync(LifecycleArrange.Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var personId = (await db.Entitlements.AsNoTracking().SingleAsync(e => e.Id == old, ct)).PersonId;
        var renewal = await db.Entitlements.AsNoTracking().SingleOrDefaultAsync(e => e.PersonId == personId && e.Id != old && e.Justification.StartsWith("Renewal of "), ct);
        Assert.True(renewal is null || renewal.Justification.Contains("Original reason: Lifecycle window test", StringComparison.Ordinal));
        return renewal;
    }

    private async Task<bool> AuditedAsync(EntitlementId renewal, CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync(LifecycleArrange.Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var subject = renewal.Value.ToString();
        return await db.AuditEntries.AsNoTracking().AnyAsync(a => a.Action == "entitlement.renewal_requested" && a.SubjectId == subject && a.ActorId == "renewal-scheduler", ct);
    }
}
