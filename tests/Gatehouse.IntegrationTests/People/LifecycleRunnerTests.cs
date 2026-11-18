using Gatehouse.Application.People.Lifecycle;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Credentials;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.People;

/// <summary>The lifecycle runner on nordlys; the seed has no ended eligibility, ended entitlements or renewals in the lead window.</summary>
public sealed class LifecycleRunnerTests(PostgresFixture postgres) : IAsyncLifetime
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
    public async Task Runner_terminates_people_past_eligibility_end()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var (personId, cardId, _) = await _arrange.SponsoredAsync(DemoUser.Mikkel, grants: 1, ct, eligibilityEnd: now.AddMinutes(-1));

        var result = await RunAsync(now, ct);

        Assert.True(result.Terminated >= 1);
        await using var check = await _api.CreateTenantScopeAsync(LifecycleArrange.Tenant);
        var read = check.ServiceProvider.GetRequiredService<TenantDbContext>();
        Assert.Equal(PersonStatus.Terminated, (await read.Persons.AsNoTracking().SingleAsync(p => p.Id == personId, ct)).Status);
        Assert.Equal(CardState.Revoked, (await read.Cards.AsNoTracking().SingleAsync(c => c.Id == cardId, ct)).State);
        Assert.Equal(EntitlementStatus.Revoked, (await read.Entitlements.AsNoTracking().SingleAsync(e => e.PersonId == personId, ct)).Status);
        var subject = personId.Value.ToString();
        Assert.True(await read.AuditEntries.AsNoTracking().AnyAsync(a => a.Action == "person.terminated" && a.SubjectId == subject && a.ActorId == "termination", ct));
        Assert.Equal(0, (await RunAsync(now, ct)).Terminated);
    }

    [Fact]
    public async Task Runner_expires_ended_entitlements()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var personId = await _arrange.ExternalAsync(now.AddDays(-20), now.AddDays(60), ct);
        var ended = await _arrange.ApprovedAsync(personId, now.AddDays(-10), now.AddMinutes(-5), ct);

        var result = await RunAsync(now, ct);

        Assert.True(result.Expired >= 1);
        await using var scope = await _api.CreateTenantScopeAsync(LifecycleArrange.Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var entitlement = await db.Entitlements.AsNoTracking().SingleAsync(e => e.Id == ended, ct);
        Assert.Equal(EntitlementStatus.Expired, entitlement.Status);
        Assert.Equal(PersonStatus.Active, (await db.Persons.AsNoTracking().SingleAsync(p => p.Id == personId, ct)).Status);
    }

    [Fact]
    public async Task Runner_expiry_plans_revoke_for_confirmed_grant()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var personId = await _arrange.ExternalAsync(now.AddDays(-20), now.AddDays(60), ct);
        var ended = await _arrange.ApprovedAsync(personId, now.AddDays(-10), now.AddMinutes(-5), ct, confirmedGrant: true);

        await RunAsync(now, ct);

        await using var scope = await _api.CreateTenantScopeAsync(LifecycleArrange.Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var revoke = await db.Fulfillments.AsNoTracking().SingleAsync(f => f.EntitlementId == ended && f.Kind == FulfillmentKind.Revoke, ct);
        Assert.Equal(_arrange.Hq, revoke.PacsInstanceId);
        var subject = ended.Value.ToString();
        Assert.True(await db.AuditEntries.AsNoTracking().AnyAsync(a => a.Action == "entitlement.expired" && a.SubjectId == subject && a.ActorId == "expiry", ct));
    }

    private async Task<LifecycleRunResult> RunAsync(DateTimeOffset now, CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync(LifecycleArrange.Tenant);
        return await scope.ServiceProvider.GetRequiredService<LifecycleRunner>().RunTenantAsync(now, ct);
    }
}
