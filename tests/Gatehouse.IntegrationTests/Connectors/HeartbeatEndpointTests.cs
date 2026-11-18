using System.Net;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Connectors;

public sealed class HeartbeatEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatewayFactory _factory = new(postgres);

    public async ValueTask InitializeAsync()
    {
        await _factory.InitializeAsync();
        await ConnectorTestEnrollment.ResetAsync(_factory);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Heartbeat_marks_instance_online()
    {
        var ct = TestContext.Current.CancellationToken;
        using var cert = await ConnectorTestEnrollment.EnrollAsync(_factory, "nordlys", "HQ-Copenhagen");
        using var client = _factory.CreateClient();
        var response = await ConnectorTestEnrollment.PostAsync(client, "/connector/v1/heartbeat", ConnectorTestEnrollment.NewHeartbeat(), cert);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = await ConnectorTestEnrollment.ScopeAsync(_factory, "nordlys");
        var pacs = await scope.ServiceProvider.GetRequiredService<TenantDbContext>().PacsInstances.AsNoTracking().SingleAsync(p => p.Name == "HQ-Copenhagen", ct);
        Assert.True(pacs.IsOnline(DateTimeOffset.UtcNow));
        Assert.Equal("1.0.0", pacs.AgentVersion);
    }

    [Fact]
    public async Task Capability_change_enqueues_message_once()
    {
        var ct = TestContext.Current.CancellationToken;
        using var cert = await ConnectorTestEnrollment.EnrollAsync(_factory, "nordlys", "HQ-Copenhagen");
        using var client = _factory.CreateClient();
        await using (var clear = await ConnectorTestEnrollment.ScopeAsync(_factory, "nordlys"))
        {
            await clear.ServiceProvider.GetRequiredService<TenantDbContext>().OutboxMessages.ExecuteDeleteAsync(ct);
        }

        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await ConnectorTestEnrollment.PostAsync(client, "/connector/v1/heartbeat", ConnectorTestEnrollment.NewHeartbeat(), cert)).StatusCode);
        }

        await using var scope = await ConnectorTestEnrollment.ScopeAsync(_factory, "nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        Assert.Equal(1, await db.OutboxMessages.CountAsync(m => m.Type.Contains("ConnectorCapabilitiesReported"), ct));

        Assert.Equal(HttpStatusCode.OK, (await ConnectorTestEnrollment.PostAsync(client, "/connector/v1/heartbeat", ConnectorTestEnrollment.NewHeartbeat(pinWrite: true), cert)).StatusCode);
        Assert.Equal(2, await db.OutboxMessages.CountAsync(m => m.Type.Contains("ConnectorCapabilitiesReported"), ct));
    }

    [Fact]
    public async Task Heartbeat_enqueues_connector_heartbeat_received()
    {
        var ct = TestContext.Current.CancellationToken;
        using var cert = await ConnectorTestEnrollment.EnrollAsync(_factory, "nordlys", "HQ-Copenhagen");
        using var client = _factory.CreateClient();
        await using (var clear = await ConnectorTestEnrollment.ScopeAsync(_factory, "nordlys"))
        {
            await clear.ServiceProvider.GetRequiredService<TenantDbContext>().OutboxMessages.ExecuteDeleteAsync(ct);
        }

        Assert.Equal(HttpStatusCode.OK, (await ConnectorTestEnrollment.PostAsync(client, "/connector/v1/heartbeat", ConnectorTestEnrollment.NewHeartbeat(), cert)).StatusCode);

        await using var scope = await ConnectorTestEnrollment.ScopeAsync(_factory, "nordlys");
        var message = await scope.ServiceProvider.GetRequiredService<TenantDbContext>().OutboxMessages.AsNoTracking()
            .SingleAsync(m => m.Type == "ConnectorHeartbeatReceived", ct);
        Assert.Contains("agentVersion", message.Payload, StringComparison.Ordinal);
        Assert.Contains("1.0.0", message.Payload, StringComparison.Ordinal);
    }
}

