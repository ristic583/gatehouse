extern alias GatehouseWorkers;
using System.Diagnostics;
using Gatehouse.Application.Common.Messaging;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.Fulfillment.Events;
using Gatehouse.Domain.Tenancy;
using Gatehouse.Infrastructure;
using Gatehouse.Infrastructure.Messaging;
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
using Npgsql;
using StackExchange.Redis;

namespace Gatehouse.IntegrationTests.Messaging;

/// <summary>
/// M11: the relay worker with the Redis pending-outbox signal visits only signalled tenants, so idle tenant databases are not reconnected
/// on every 250 ms tick (PR #27 releases each visited tenant's pool), while a new message is still relayed within about one tick.
/// </summary>
public sealed class OutboxRelayWorkerTests(PostgresFixture postgres, RedisFixture redis)
{
    private const int IdleTenants = 20;
    private static readonly TimeSpan Warmup = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(3);

    [Fact]
    public async Task Relay_over_idle_tenants_with_signal_opens_far_fewer_connections_than_polling()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var tenancy = await TenancyTestHost.CreateAsync(postgres, ct);
        var prefix = "idle" + Guid.NewGuid().ToString("N")[..8];
        await AddTenantsAsync(tenancy, prefix, IdleTenants, ct);

        var polling = await MeasureConnectsPerSecondAsync(tenancy, prefix, signal: null, ct);
        var signalled = await MeasureConnectsPerSecondAsync(tenancy, prefix, redis.Connection, ct);

        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{IdleTenants} idle tenants: polling every tick {polling:F1} connects/s, with signal {signalled:F1} connects/s");
        Assert.True(polling >= IdleTenants, $"Polling baseline {polling:F1} connects/s is lower than expected; the measurement is broken.");
        Assert.True(signalled * 10 < polling, $"With signal {signalled:F1} connects/s, polling {polling:F1} connects/s.");
    }

    [Fact]
    public async Task Relay_with_signal_publishes_new_message_of_idle_tenant_within_one_second()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var tenancy = await TenancyTestHost.CreateAsync(postgres, ct);
        var tenants = await AddTenantsAsync(tenancy, "late" + Guid.NewGuid().ToString("N")[..8], 3, ct);
        using var host = await StartWorkerAsync(tenancy, redis.Connection, ct);
        var harness = host.Services.GetRequiredService<ITestHarness>();
        try
        {
            await Task.Delay(Warmup, ct); // past the first sweep; the sweep interval (60 s) cannot deliver it

            Guid id;
            var stopwatch = Stopwatch.StartNew();
            await using (var scope = await host.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(tenants[1], ct))
            {
                scope.ServiceProvider.GetRequiredService<IOutboxWriter>().Enqueue(
                    new FulfillmentDesired(FulfillmentId.New(), EntitlementId.New(), PacsInstanceId.New(), FulfillmentKind.Grant, CommandPriority.Normal, 1, DateTimeOffset.UtcNow),
                    CommandPriority.Normal);
                var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
                id = db.OutboxMessages.Local.Single().Id;
                await db.SaveChangesAsync(ct);
            }

            while (!harness.Published.Select<FulfillmentDesired>(ct).Any(m => m.Context.MessageId == id) && stopwatch.Elapsed < TimeSpan.FromSeconds(5))
            {
                await Task.Delay(10, ct);
            }

            stopwatch.Stop();
            TestContext.Current.TestOutputHelper?.WriteLine($"Relayed after {stopwatch.ElapsedMilliseconds} ms");
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"Relayed after {stopwatch.ElapsedMilliseconds} ms.");
        }
        finally
        {
            await harness.Stop(CancellationToken.None);
        }
    }

    private async Task<List<TenantId>> AddTenantsAsync(TenancyTestHost tenancy, string prefix, int count, CancellationToken ct)
    {
        // One migrated template (unpooled, so no connection stays open), cloned for every tenant.
        var template = PostgresFixture.UniqueName("outboxtpl");
        var connectionString = await postgres.CreateDatabaseAsync(template, ct);
        await using (var db = new TenantDbContext(new DbContextOptionsBuilder<TenantDbContext>()
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .Options))
        {
            await db.Database.MigrateAsync(ct);
        }

        var tenants = new List<TenantId>();
        for (var i = 0; i < count; i++)
        {
            tenants.Add(await tenancy.AddTenantAsync($"{prefix}-{i}", TenantStatus.Active, ct, template));
        }

        return tenants;
    }

    private async Task<double> MeasureConnectsPerSecondAsync(TenancyTestHost tenancy, string prefix, IConnectionMultiplexer? signal, CancellationToken ct)
    {
        using var host = await StartWorkerAsync(tenancy, signal, ct);
        var harness = host.Services.GetRequiredService<ITestHarness>();
        try
        {
            await Task.Delay(Warmup, ct);
            var before = await SessionsAsync(prefix, ct);
            await Task.Delay(Window, ct);
            var after = await SessionsAsync(prefix, ct);
            return (after - before) / Window.TotalSeconds;
        }
        finally
        {
            await harness.Stop(CancellationToken.None);
        }
    }

    private static async Task<IHost> StartWorkerAsync(TenancyTestHost tenancy, IConnectionMultiplexer? signal, CancellationToken ct)
    {
        var config = tenancy.Services.GetRequiredService<IConfiguration>();
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
            ["Outbox:RelayIntervalMs"] = "250",
            [OutboxRelayScheduler.SweepIntervalSetting] = "60000",
        });
        if (signal is not null)
        {
            builder.Services.AddSingleton(signal);
        }

        builder.AddGatehouseCatalog();
        builder.AddGatehouseTenancy();
        builder.Services.AddMassTransitTestHarness();
        builder.Services.AddHostedService<GatehouseWorkers::Gatehouse.Workers.OutboxRelayWorker>();
        var host = builder.Build();
        await host.Services.GetRequiredService<ITestHarness>().Start(); // starts the hosted services, the relay worker included
        ct.ThrowIfCancellationRequested();
        return host;
    }

    // pg_stat_database.sessions counts every connection ever established to the database.
    private async Task<long> SessionsAsync(string prefix, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionStringFor("postgres"));
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT coalesce(sum(sessions), 0)::bigint FROM pg_stat_database WHERE starts_with(datname, @prefix)";
        command.Parameters.AddWithValue("prefix", "t_" + prefix + "_");
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }
}
