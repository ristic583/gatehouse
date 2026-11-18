using System.Net.Http.Json;
using Gatehouse.Application.Access.Dtos;
using Gatehouse.Application.Fulfillment.MonitorRevocations;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tasks;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Fulfillment;

public sealed class RevocationMonitorTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatehouseApiFactory _factory = new(postgres);

    public ValueTask InitializeAsync() => _factory.InitializeAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private async Task<Guid> EntitlementAsync(string areaCode)
    {
        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var ct = TestContext.Current.CancellationToken;
        var annaId = (await db.Persons.SingleAsync(p => p.Email == DemoUser.Anna.Email, ct)).Id;
        var areaId = (await db.Areas.SingleAsync(a => a.Code == areaCode, ct)).Id;
        return (await db.Entitlements.AsNoTracking().SingleAsync(e => e.PersonId == annaId && e.AreaId == areaId, ct)).Id.Value;
    }

    private async Task<(Guid EntitlementId, DateTimeOffset RevokedAt)> RevokeAsync(DemoUser by, string areaCode, string action)
    {
        var id = await EntitlementAsync(areaCode);
        using var client = _factory.CreateClientFor(by);
        using var response = await client.PostAsJsonAsync(new Uri($"/api/v1/access-requests/{id}/{action}", UriKind.Relative), new RevokeEntitlementDto("Left"), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var e = await db.Entitlements.AsNoTracking().SingleAsync(x => x.Id == new Gatehouse.Domain.Access.EntitlementId(id), TestContext.Current.CancellationToken);
        return (id, e.Revocation!.RevokedAt);
    }

    private async Task<int> DetectAsync(DateTimeOffset now)
    {
        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        return await scope.ServiceProvider.GetRequiredService<DetectUnconfirmedRevocationsHandler>()
            .HandleAsync(new DetectUnconfirmedRevocationsCommand(now), TestContext.Current.CancellationToken);
    }

    private async Task<List<StaffTask>> TasksAsync()
    {
        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        return await db.StaffTasks.AsNoTracking().Where(t => t.Kind == StaffTaskKind.RevocationUnconfirmed).ToListAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Fresh_revocation_is_not_flagged()
    {
        var (_, at) = await RevokeAsync(DemoUser.Erik, "CPH-OFFICE-2", "revoke");
        Assert.Equal(0, await DetectAsync(at.AddMinutes(1)));
        Assert.Empty(await TasksAsync());
    }

    [Fact]
    public async Task Unconfirmed_revocation_past_threshold_opens_task_and_alert()
    {
        var (_, at) = await RevokeAsync(DemoUser.Erik, "CPH-OFFICE-2", "revoke");
        Assert.Equal(1, await DetectAsync(at.AddMinutes(16)));

        Assert.Single(await TasksAsync(), t => t.Status != StaffTaskStatus.Resolved);
        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var ct = TestContext.Current.CancellationToken;
        Assert.Contains(await db.OutboxMessages.Select(m => m.Type).ToListAsync(ct), t => t.Contains("RevocationUnconfirmedAlert", StringComparison.Ordinal));
        var entries = await db.AuditEntries.AsNoTracking().Where(e => e.Action == "revocation.unconfirmed_alert").ToListAsync(ct);
        Assert.NotEmpty(entries);
        Assert.All(entries, e => Assert.Equal(AuditActorKind.System, e.ActorKind));
    }

    [Fact]
    public async Task Running_twice_opens_one_task()
    {
        var (_, at) = await RevokeAsync(DemoUser.Erik, "CPH-OFFICE-2", "revoke");
        Assert.Equal(1, await DetectAsync(at.AddMinutes(16)));
        Assert.Equal(0, await DetectAsync(at.AddMinutes(17)));
        Assert.Single(await TasksAsync());
    }

    [Fact]
    public async Task Emergency_revocation_uses_shorter_threshold()
    {
        var (_, at) = await RevokeAsync(DemoUser.Jonas, "CPH-LOBBY", "emergency-revoke");
        Assert.Equal(1, await DetectAsync(at.AddMinutes(6)));
    }

    [Fact]
    public async Task Confirming_the_revocation_resolves_the_task()
    {
        var (id, at) = await RevokeAsync(DemoUser.Erik, "CPH-OFFICE-2", "revoke");
        Assert.Equal(1, await DetectAsync(at.AddMinutes(16)));

        Guid fulfillmentId;
        await using (var scope = await _factory.CreateTenantScopeAsync("nordlys"))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var eid = new Gatehouse.Domain.Access.EntitlementId(id);
            fulfillmentId = (await db.Fulfillments.AsNoTracking().SingleAsync(f => f.EntitlementId == eid && f.Kind == Gatehouse.Domain.Fulfillment.FulfillmentKind.Revoke, TestContext.Current.CancellationToken)).Id.Value;
        }

        using var jonas = _factory.CreateClientFor(DemoUser.Jonas);
        using var confirmed = await jonas.PostAsJsonAsync(new Uri($"/api/v1/fulfillments/{fulfillmentId}/confirm", UriKind.Relative), new { }, TestContext.Current.CancellationToken);
        confirmed.EnsureSuccessStatusCode();
        await _factory.DrainMessagesAsync("nordlys");

        Assert.All(await TasksAsync(), t => Assert.Equal(StaffTaskStatus.Resolved, t.Status));
    }

    [Fact]
    public async Task Runner_visits_every_active_tenant()
    {
        var result = await _factory.Services.GetRequiredService<RevocationMonitorRunner>().RunOnceAsync(TestContext.Current.CancellationToken);
        Assert.Contains(new TenantId("nordlys"), result.Keys);
        Assert.Contains(new TenantId("bricks"), result.Keys);
        Assert.DoesNotContain(new TenantId("frozen"), result.Keys);
    }
}
