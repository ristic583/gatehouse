using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.Fulfillment.Events;
using Gatehouse.Domain.Tenancy;
using Gatehouse.Infrastructure;
using Gatehouse.Infrastructure.Messaging;
using Gatehouse.Infrastructure.Messaging.Outbox;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.Infrastructure.Security;
using Gatehouse.IntegrationTests.Infrastructure;
using Gatehouse.IntegrationTests.Tenancy;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Gatehouse.IntegrationTests.Messaging;

public sealed class OutboxRelayTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Publishes_pending_rows_with_tenant_header()
    {
        await using var env = await Env.CreateAsync(postgres);
        await env.EnqueueAsync(Desired(), CommandPriority.Normal);

        Assert.Equal(1, await env.RelayAsync());

        Assert.True(await env.Harness.Published.Any<FulfillmentDesired>(TestContext.Current.CancellationToken));
        var published = env.Harness.Published.Select<FulfillmentDesired>(TestContext.Current.CancellationToken).Single();
        Assert.Equal(env.Tenant.Value, published.Context.Headers.Get<string>("gh-tenant-id"));
    }

    [Fact]
    public async Task High_priority_rows_publish_first()
    {
        await using var env = await Env.CreateAsync(postgres);
        var normal = await env.EnqueueAsync(Desired(), CommandPriority.Normal);
        var high = await env.EnqueueAsync(Desired(), CommandPriority.High);

        await env.RelayAsync();

        var order = env.Harness.Published.Select<FulfillmentDesired>(TestContext.Current.CancellationToken)
            .Select(m => m.Context.MessageId).ToList();
        Assert.Equal([high, normal], order);
    }

    [Fact]
    public async Task Publish_failure_schedules_backoff()
    {
        await using var env = await Env.CreateAsync(postgres);
        var ct = TestContext.Current.CancellationToken;
        await using (var scope = await env.Scopes.CreateScopeAsync(env.Tenant, ct))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            db.OutboxMessages.Add(new OutboxMessage
            {
                Id = Guid.CreateVersion7(),
                OccurredAt = DateTimeOffset.UtcNow,
                Type = "NoSuchType",
                NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            });
            await db.SaveChangesAsync(ct);
        }

        Assert.Equal(0, await env.RelayAsync());

        await using var check = await env.Scopes.CreateScopeAsync(env.Tenant, ct);
        var row = await check.ServiceProvider.GetRequiredService<TenantDbContext>().OutboxMessages.AsNoTracking().SingleAsync(ct);
        Assert.Equal(1, row.Attempts);
        Assert.Null(row.DispatchedAt);
        Assert.NotNull(row.LastError);
        Assert.True(row.NextAttemptAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Dispatched_rows_are_not_republished()
    {
        await using var env = await Env.CreateAsync(postgres);
        await env.EnqueueAsync(Desired(), CommandPriority.Normal);

        Assert.Equal(1, await env.RelayAsync());
        Assert.Equal(0, await env.RelayAsync());
    }

    private static FulfillmentDesired Desired() =>
        new(FulfillmentId.New(), EntitlementId.New(), PacsInstanceId.New(), FulfillmentKind.Grant, CommandPriority.Normal, 1, DateTimeOffset.UtcNow);

    private sealed class Env : IAsyncDisposable
    {
        private readonly IHost _host;

        private Env(IHost host, TenantId tenant, ITestHarness harness)
        {
            _host = host;
            Tenant = tenant;
            Harness = harness;
        }

        public TenantId Tenant { get; }

        public ITestHarness Harness { get; }

        public ITenantScopeFactory Scopes => _host.Services.GetRequiredService<ITenantScopeFactory>();

        public static async Task<Env> CreateAsync(PostgresFixture postgres)
        {
            var ct = TestContext.Current.CancellationToken;
            await using var provisioning = await TenancyTestHost.CreateAsync(postgres, ct);
            var tenant = await provisioning.AddTenantAsync("relay-" + Guid.NewGuid().ToString("N")[..8], TenantStatus.Active, ct);
            var config = provisioning.Services.GetRequiredService<IConfiguration>();

            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                EnvironmentName = Environments.Development,
                DisableDefaults = true,
            });
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:catalog"] = config.GetConnectionString("catalog"),
                ["ConnectionStrings:postgres"] = config.GetConnectionString("postgres"),
                [AesGcmSecretProtector.MasterKeySetting] = TenancyTestHost.MasterKey,
            });
            builder.AddGatehouseCatalog();
            builder.AddGatehouseTenancy();
            builder.Services.AddMassTransitTestHarness();
            var host = builder.Build();
            var harness = host.Services.GetRequiredService<ITestHarness>();
            await harness.Start();
            return new Env(host, tenant, harness);
        }

        public async Task<Guid> EnqueueAsync(object message, CommandPriority priority)
        {
            var ct = TestContext.Current.CancellationToken;
            await using var scope = await Scopes.CreateScopeAsync(Tenant, ct);
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            scope.ServiceProvider.GetRequiredService<Gatehouse.Application.Common.Messaging.IOutboxWriter>().Enqueue(message, priority);
            var id = db.ChangeTracker.Entries<OutboxMessage>().Single().Entity.Id;
            await db.SaveChangesAsync(ct);
            return id;
        }

        public Task<int> RelayAsync() =>
            _host.Services.GetRequiredService<OutboxRelay>().RunOnceAsync(Tenant, TestContext.Current.CancellationToken);

        public async ValueTask DisposeAsync()
        {
            await Harness.Stop();
            _host.Dispose();
        }
    }
}
