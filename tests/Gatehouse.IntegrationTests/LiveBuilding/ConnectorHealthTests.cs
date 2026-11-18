using System.Net;
using System.Net.Http.Json;
using Gatehouse.Application.Common.Messaging;
using Gatehouse.Application.Connectors.Messages;
using Gatehouse.Application.LiveBuilding;
using Gatehouse.Application.LiveBuilding.ConnectorHealth;
using Gatehouse.Application.LiveBuilding.Dtos;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.LiveBuilding;

public sealed class ConnectorHealthTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Tenant = "nordlys";
    private const string Instance = "HQ-Copenhagen";

    private readonly GatehouseApiFactory _api = new(postgres);
    private readonly string _marker = "health-test:" + Guid.CreateVersion7();
    private DateTimeOffset? _lastHeartbeatAt;
    private DateTimeOffset? _offlineNotifiedAt;
    private FulfillmentMode _mode;

    public async ValueTask InitializeAsync()
    {
        await _api.InitializeAsync();
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var instance = await db.PacsInstances.AsNoTracking().SingleAsync(p => p.Name == Instance);
        _lastHeartbeatAt = instance.LastHeartbeatAt;
        _offlineNotifiedAt = instance.OfflineNotifiedAt;
        _mode = instance.FulfillmentMode;
        await db.PacsInstances.Where(p => p.Name == Instance).ExecuteUpdateAsync(s => s.SetProperty(p => p.FulfillmentMode, FulfillmentMode.Connector));
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    [Fact]
    public async Task Heartbeat_updates_cache_and_publishes()
    {
        var ct = TestContext.Current.CancellationToken;
        var messaging = await _api.MessagingAsync();
        var received = new List<LiveUpdateDto>();
        await messaging.Services.GetRequiredService<ILiveUpdateSubscriber>().SubscribeAsync((tenant, update) =>
        {
            lock (received)
            {
                if (tenant == Tenant && update.Kind == LiveUpdateKind.ConnectorHealth)
                {
                    received.Add(update);
                }
            }

            return Task.CompletedTask;
        }, ct);
        var (instanceId, siteId) = await InstanceAsync();
        var version = "health-" + Guid.NewGuid().ToString("N")[..8];

        await using (var scope = await _api.CreateTenantScopeAsync(Tenant))
        {
            scope.ServiceProvider.GetRequiredService<IOutboxWriter>().Enqueue(
                new ConnectorHeartbeatReceived(Tenant, instanceId, new ConnectorId(Guid.CreateVersion7()), DateTimeOffset.UtcNow, version, 7, false),
                CommandPriority.Normal);
            await scope.ServiceProvider.GetRequiredService<TenantDbContext>().SaveChangesAsync(ct);
        }

        await _api.DrainMessagesAsync(Tenant);
        Assert.True(await messaging.Harness.Consumed.Any<ConnectorHeartbeatReceived>(m => m.Context.Message.AgentVersion == version, ct));

        await using (var scope = await messaging.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(new TenantId(Tenant), ct))
        {
            var cached = (await scope.ServiceProvider.GetRequiredService<IConnectorHealthCache>().GetManyAsync([instanceId], ct))[instanceId];
            Assert.Equal(version, cached.AgentVersion);
            Assert.Equal(7, cached.QueueDepth);
            Assert.False(cached.PacsReachable);
        }

        LiveUpdateDto update;
        lock (received)
        {
            update = Assert.Single(received, u => u.Connector!.AgentQueueDepth == 7 && u.Connector.PacsInstanceId == instanceId.Value);
        }

        Assert.Equal(siteId, update.SiteId);
        Assert.False(update.Connector!.PacsReachable);
    }

    [Fact]
    public async Task Sweeper_raises_went_offline_once()
    {
        var ct = TestContext.Current.CancellationToken;
        var start = DateTimeOffset.UtcNow.AddSeconds(-1);
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        await db.PacsInstances.Where(p => p.Name == Instance).ExecuteUpdateAsync(
            s => s.SetProperty(p => p.LastHeartbeatAt, DateTimeOffset.UtcNow.AddMinutes(-10)).SetProperty(p => p.OfflineNotifiedAt, (DateTimeOffset?)null), ct);
        var handler = scope.ServiceProvider.GetRequiredService<SweepOfflineConnectorsHandler>();

        var first = await handler.HandleAsync(new SweepOfflineConnectorsCommand(DateTimeOffset.UtcNow), ct);
        var second = await handler.HandleAsync(new SweepOfflineConnectorsCommand(DateTimeOffset.UtcNow.AddSeconds(30)), ct);

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        var (instanceId, _) = await InstanceAsync();
        var events = await db.OutboxMessages.AsNoTracking()
            .Where(m => m.Type == "ConnectorWentOffline" && m.OccurredAt >= start).Select(m => m.Payload).ToListAsync(ct);
        Assert.Single(events, p => p.Contains(instanceId.Value.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Endpoint_reports_queue_and_ack_latency()
    {
        var ct = TestContext.Current.CancellationToken;
        var (instanceId, siteId) = await InstanceAsync();
        int baseline;
        var now = DateTimeOffset.UtcNow;
        await using (var scope = await _api.CreateTenantScopeAsync(Tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            baseline = await db.ConnectorCommands.CountAsync(c => c.PacsInstanceId == instanceId && c.Status == ConnectorCommandStatus.Queued, ct);
            db.ConnectorCommands.Add(Command(instanceId, 1, now));
            db.ConnectorCommands.Add(Command(instanceId, 2, now));
            var acked = Command(instanceId, 3, now.AddSeconds(5));
            Assert.True(acked.Deliver(now.AddSeconds(5.5)).IsSuccess);
            acked.ApplyAck(CommandAckStatus.Applied, "ref", null, null, false, now.AddSeconds(6.5));
            db.ConnectorCommands.Add(acked);
            await db.SaveChangesAsync(ct);
            await db.PacsInstances.Where(p => p.Id == instanceId).ExecuteUpdateAsync(
                s => s.SetProperty(p => p.LastHeartbeatAt, DateTimeOffset.UtcNow).SetProperty(p => p.OfflineNotifiedAt, (DateTimeOffset?)null), ct);
        }

        using var jonas = _api.CreateClientFor(DemoUser.Jonas);
        var list = await jonas.GetFromJsonAsync<List<ConnectorHealthDto>>(new Uri($"/api/v1/live/connectors?siteId={siteId}", UriKind.Relative), ApiJson.Options, ct);

        var hq = Assert.Single(list!, c => c.PacsInstanceId == instanceId.Value);
        Assert.True(hq.Online);
        Assert.Equal(baseline + 2, hq.QueuedCommands);
        Assert.Equal(1500, hq.LastAckLatencyMs);
        Assert.Equal(FulfillmentMode.Connector, hq.FulfillmentMode);

        using var freja = _api.CreateClientFor(DemoUser.Freja);
        var all = await freja.GetFromJsonAsync<List<ConnectorHealthDto>>(new Uri("/api/v1/live/connectors", UriKind.Relative), ApiJson.Options, ct);
        Assert.Contains(all!, c => c.FulfillmentMode == FulfillmentMode.Manual && c.Online && c.AgentVersion is null);

        using var anna = _api.CreateClientFor(DemoUser.Anna);
        using var forbidden = await anna.GetAsync(new Uri("/api/v1/live/connectors", UriKind.Relative), ct);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    private ConnectorCommand Command(PacsInstanceId instanceId, long sequence, DateTimeOffset createdAt) =>
        ConnectorCommand.Create(instanceId, "UpsertCardholder", _marker, sequence, CommandPriority.Normal, $"{_marker}:{sequence}", "{}", null, createdAt);

    private async Task<(PacsInstanceId InstanceId, Guid SiteId)> InstanceAsync()
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var instance = await db.PacsInstances.AsNoTracking().SingleAsync(p => p.Name == Instance, TestContext.Current.CancellationToken);
        var site = await db.Sites.AsNoTracking().SingleAsync(s => s.Code == "CPH-HQ", TestContext.Current.CancellationToken);
        return (instance.Id, site.Id.Value);
    }
}
