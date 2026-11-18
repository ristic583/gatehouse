using System.Net;
using Gatehouse.Connector.Contracts.Events;
using Gatehouse.Domain.Tasks;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Connectors;

/// <summary>GH-CONN-03: refused local scheduled removals reported in the heartbeat become one staff task each.</summary>
public sealed class ConnectorHeartbeatTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatewayFactory _factory = new(postgres);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _factory.InitializeAsync();
        await ConnectorTestEnrollment.ResetAsync(_factory);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Refused_removal_opens_task_once()
    {
        using var cert = await ConnectorTestEnrollment.EnrollAsync(_factory, "nordlys", "HQ-Copenhagen");
        using var client = _factory.CreateClient();
        var refused = new RefusedRemoval("person-1:AL-SERVER", "pacs.level_locked", DateTimeOffset.UtcNow.AddMinutes(-20), 1);

        await HeartbeatAsync(client, cert, [refused]);
        await HeartbeatAsync(client, cert, [refused with { Attempts = 2 }]);

        await using var scope = await ConnectorTestEnrollment.ScopeAsync(_factory, "nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var task = await db.StaffTasks.AsNoTracking().SingleAsync(t => t.Kind == StaffTaskKind.ScheduledRemovalRefused, Ct);
        Assert.Equal(StaffTaskStatus.Open, task.Status);
        Assert.StartsWith("ScheduledRemoval:", task.SubjectType, StringComparison.Ordinal);
        Assert.Equal(1, await db.AuditEntries.CountAsync(a => a.Action == "task.removal_refused_reported" && a.SubjectId == task.Id.Value.ToString(), Ct));
    }

    [Fact]
    public async Task Task_resolves_when_refusal_disappears()
    {
        using var cert = await ConnectorTestEnrollment.EnrollAsync(_factory, "nordlys", "HQ-Copenhagen");
        using var client = _factory.CreateClient();
        var first = new RefusedRemoval("person-1:AL-SERVER", "pacs.level_locked", DateTimeOffset.UtcNow.AddMinutes(-20), 1);
        var second = new RefusedRemoval("person-2:AL-LAB", "pacs.level_locked", DateTimeOffset.UtcNow.AddMinutes(-5), 1);
        await HeartbeatAsync(client, cert, [first, second]);

        // An older agent (no refusedRemovals field) changes nothing; a report without the first one resolves only that task.
        Assert.Equal(HttpStatusCode.OK, (await ConnectorTestEnrollment.PostAsync(client, "/connector/v1/heartbeat", ConnectorTestEnrollment.NewHeartbeat(), cert)).StatusCode);
        Assert.Equal(2, await OpenTaskCountAsync());
        await HeartbeatAsync(client, cert, [second]);

        Assert.Equal(1, await OpenTaskCountAsync());
        await HeartbeatAsync(client, cert, []);
        Assert.Equal(0, await OpenTaskCountAsync());
        await using var scope = await ConnectorTestEnrollment.ScopeAsync(_factory, "nordlys");
        Assert.All(
            await scope.ServiceProvider.GetRequiredService<TenantDbContext>().StaffTasks.AsNoTracking()
                .Where(t => t.Kind == StaffTaskKind.ScheduledRemovalRefused).ToListAsync(Ct),
            t => Assert.Equal(StaffTaskResolutionAction.AutoResolved, t.ResolutionAction));
    }

    private static async Task HeartbeatAsync(HttpClient client, System.Security.Cryptography.X509Certificates.X509Certificate2 cert, RefusedRemoval[] refused)
    {
        var heartbeat = ConnectorTestEnrollment.NewHeartbeat() with { RefusedRemovals = refused };
        Assert.Equal(HttpStatusCode.OK, (await ConnectorTestEnrollment.PostAsync(client, "/connector/v1/heartbeat", heartbeat, cert)).StatusCode);
    }

    private async Task<int> OpenTaskCountAsync()
    {
        await using var scope = await ConnectorTestEnrollment.ScopeAsync(_factory, "nordlys");
        return await scope.ServiceProvider.GetRequiredService<TenantDbContext>().StaffTasks
            .CountAsync(t => t.Kind == StaffTaskKind.ScheduledRemovalRefused && t.Status != StaffTaskStatus.Resolved, Ct);
    }
}
