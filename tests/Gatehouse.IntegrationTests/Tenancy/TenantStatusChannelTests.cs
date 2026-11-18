extern alias GatehouseApi;

using System.Diagnostics;
using System.Net.Http.Json;
using Gatehouse.Application.Platform;
using Gatehouse.Application.Platform.Dtos;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Platform;
using Gatehouse.Domain.Tenancy;
using Gatehouse.Infrastructure.Persistence.Catalog;
using Gatehouse.Infrastructure.Tenancy;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace Gatehouse.IntegrationTests.Tenancy;

/// <summary>
/// GH-AUTH-03: each "process" is its own service provider (own memory cache, own subscriber) on the shared Redis; every test uses
/// its own channel name so parallel tests never see each other's messages.
/// </summary>
public sealed class TenantStatusChannelTests(RedisFixture redis, PostgresFixture postgres)
{
    private static readonly TimeSpan PropagationBound = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task Suspension_evicts_cache_in_other_process_within_two_seconds()
    {
        var ct = TestContext.Current.CancellationToken;
        var channel = NewChannel();
        await using var api = await StatusProcess.StartAsync(redis.Connection, channel, ct);
        await using var gateway = await StatusProcess.StartAsync(redis.Connection, channel, ct);
        var tenant = new TenantId(RedisFixture.UniqueTenantId());
        gateway.CacheConnection(tenant);

        var watch = Stopwatch.StartNew();
        await api.Notifier.PublishAsync(tenant, TenantStatus.Suspended, ct);

        Assert.True(await gateway.WaitUntilEvictedAsync(tenant, PropagationBound, ct));
        Assert.True(watch.Elapsed < PropagationBound, $"took {watch.Elapsed}");
    }

    [Fact]
    public async Task Reactivation_evicts_cache_in_other_process()
    {
        var ct = TestContext.Current.CancellationToken;
        var channel = NewChannel();
        await using var api = await StatusProcess.StartAsync(redis.Connection, channel, ct);
        await using var workers = await StatusProcess.StartAsync(redis.Connection, channel, ct);
        var tenant = new TenantId(RedisFixture.UniqueTenantId());
        var other = new TenantId(RedisFixture.UniqueTenantId());
        workers.CacheConnection(tenant);
        workers.CacheConnection(other);

        await api.Notifier.PublishAsync(tenant, TenantStatus.Active, ct);

        Assert.True(await workers.WaitUntilEvictedAsync(tenant, PropagationBound, ct));
        Assert.True(workers.IsCached(other));
    }

    [Fact]
    public async Task Reconnect_clears_whole_cache()
    {
        var ct = TestContext.Current.CancellationToken;
        var clientName = "gh-status-" + Guid.NewGuid().ToString("N")[..12];
        var options = ConfigurationOptions.Parse(redis.ConnectionString);
        options.ClientName = clientName;
        await using var own = await ConnectionMultiplexer.ConnectAsync(options);
        await using var process = await StatusProcess.StartAsync(own, NewChannel(), ct);
        var first = new TenantId(RedisFixture.UniqueTenantId());
        var second = new TenantId(RedisFixture.UniqueTenantId());
        process.CacheConnection(first);
        process.CacheConnection(second);

        await KillSubscriptionConnectionAsync(clientName);

        Assert.True(await process.WaitUntilEvictedAsync(first, TimeSpan.FromSeconds(15), ct));
        Assert.False(process.IsCached(second));
    }

    [Fact]
    public async Task Message_is_published_only_after_commit()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new GatehouseApiFactory(postgres);
        await factory.InitializeAsync();
        var recorder = new CommittedStatusRecorder(factory);
        using var api = factory.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.Replace(ServiceDescriptor.Singleton<ITenantStatusNotifier>(recorder))));
        using var platform = await OperatorClientAsync(factory, api, ct);
        var basePath = "/api/v1/platform/tenants/nordlys";

        using (var suspend = await platform.PostAsJsonAsync(new Uri($"{basePath}/suspend", UriKind.Relative), new SuspendTenantRequest("Unpaid"), ApiJson.Options, ct))
        {
            suspend.EnsureSuccessStatusCode();
        }

        using (var again = await platform.PostAsJsonAsync(new Uri($"{basePath}/suspend", UriKind.Relative), new SuspendTenantRequest("Again"), ApiJson.Options, ct))
        {
            Assert.False(again.IsSuccessStatusCode);
        }

        using (var reactivate = await platform.PostAsync(new Uri($"{basePath}/reactivate", UriKind.Relative), null, ct))
        {
            reactivate.EnsureSuccessStatusCode();
        }

        // Each message saw its own status already committed (read on another connection); the refused suspend published nothing.
        Assert.Equal(
            [(TenantStatus.Suspended, TenantStatus.Suspended), (TenantStatus.Active, TenantStatus.Active)],
            recorder.Published);
    }

    private static string NewChannel() => "test:" + Guid.NewGuid().ToString("N") + ":tenant-status";

    private async Task KillSubscriptionConnectionAsync(string clientName)
    {
        var options = ConfigurationOptions.Parse(redis.ConnectionString);
        options.AllowAdmin = true;
        await using var admin = await ConnectionMultiplexer.ConnectAsync(options);
        var server = admin.GetServer(admin.GetEndPoints()[0]);
        var clients = await server.ClientListAsync();
        var ids = clients.Where(c => c.Name == clientName && c.ClientType == ClientType.PubSub).Select(c => c.Id).ToList();
        Assert.NotEmpty(ids);
        foreach (var id in ids)
        {
            await server.ClientKillAsync(id);
        }
    }

    private static async Task<HttpClient> OperatorClientAsync(GatehouseApiFactory factory, Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<GatehouseApi::Program> api, CancellationToken ct)
    {
        var email = $"op-{Guid.NewGuid():N}@platform.test";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            catalog.PlatformOperators.Add(PlatformOperator.Create(email, "Test operator", DateTimeOffset.UtcNow).Value);
            await catalog.SaveChangesAsync(ct);
        }

        var client = api.CreateClient();
        client.DefaultRequestHeaders.Add(TestPlatformAuthHandler.SubHeader, $"kc-{Guid.NewGuid():N}");
        client.DefaultRequestHeaders.Add(TestPlatformAuthHandler.EmailHeader, email);
        return client;
    }

    /// <summary>Records (published status, status committed in the catalog as seen from a fresh connection).</summary>
    private sealed class CommittedStatusRecorder(GatehouseApiFactory factory) : ITenantStatusNotifier
    {
        public List<(TenantStatus Published, TenantStatus Committed)> Published { get; } = [];

        public async Task PublishAsync(TenantId tenantId, TenantStatus status, CancellationToken ct)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var committed = await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Tenants.AsNoTracking()
                .Where(t => t.Id == tenantId).Select(t => t.Status).SingleAsync(ct);
            Published.Add((status, committed));
        }
    }

    private sealed class StatusProcess : IAsyncDisposable
    {
        private readonly ServiceProvider _services;

        private StatusProcess(ServiceProvider services) => _services = services;

        public ITenantStatusNotifier Notifier => _services.GetRequiredService<ITenantStatusNotifier>();

        private IMemoryCache Cache => _services.GetRequiredService<IMemoryCache>();

        public static async Task<StatusProcess> StartAsync(IConnectionMultiplexer redis, string channel, CancellationToken ct)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { [RedisTenantStatusChannel.ChannelSetting] = channel })
                .Build());
            services.AddLogging();
            services.AddSingleton(redis);
            services.AddTenantStatusChannel();
            var process = new StatusProcess(services.BuildServiceProvider());
            foreach (var hosted in process._services.GetServices<IHostedService>())
            {
                await hosted.StartAsync(ct);
            }

            return process;
        }

        public void CacheConnection(TenantId tenant) => Cache.Set(TenantConnectionResolver.CacheKey(tenant), "Host=cached", TimeSpan.FromMinutes(5));

        public bool IsCached(TenantId tenant) => Cache.TryGetValue(TenantConnectionResolver.CacheKey(tenant), out _);

        public async Task<bool> WaitUntilEvictedAsync(TenantId tenant, TimeSpan timeout, CancellationToken ct)
        {
            var watch = Stopwatch.StartNew();
            while (IsCached(tenant))
            {
                if (watch.Elapsed > timeout)
                {
                    return false;
                }

                await Task.Delay(20, ct);
            }

            return true;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var hosted in _services.GetServices<IHostedService>())
            {
                await hosted.StopAsync(CancellationToken.None);
            }

            await _services.DisposeAsync();
        }
    }
}
