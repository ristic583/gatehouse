using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Common;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.Infrastructure.Tenancy;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Gatehouse.IntegrationTests.Tenancy;

/// <summary>
/// M11-00 + M11 connection exhaustion: background loops over every tenant must neither hold connections to all tenants at once
/// (peak bounded by <see cref="ITenantLoop"/>) nor leave idle connections in every tenant pool.
/// </summary>
public sealed class TenantConnectionPoolTests(PostgresFixture postgres)
{
    private const int TenantCount = 50;
    private const int Workers = 3;
    private const int Rounds = 2;
    private const int IdleBound = 3;

    // Workers loops × Tenancy:MaxConcurrentTenants (4), plus one per loop for a backend that is still exiting after its close.
    private const int PeakBound = (Workers * TenantLoop.DefaultMaxConcurrentTenants) + Workers;

    [Fact]
    public async Task Tenant_loop_over_many_databases_releases_idle_connections()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await TenancyTestHost.CreateAsync(postgres, ct);
        var provisioner = host.Services.GetRequiredService<TenantDatabaseProvisioner>();
        var prefix = "pool" + Guid.NewGuid().ToString("N")[..8];
        var tenants = Enumerable.Range(0, TenantCount).Select(i => TenantId.Create($"{prefix}-{i}").Value).ToList();
        var names = tenants.Select(t => t.DatabaseName).ToArray();
        try
        {
            foreach (var tenant in tenants)
            {
                await provisioner.CreateDatabaseIfMissingAsync(tenant, ct);
            }

            // Like the workers: several loops at the same time, each walks every tenant with one disposed scope per tenant.
            using var stopSampling = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var sampler = SamplePeakAsync(names, stopSampling.Token);
            var processed = 0;
            await Task.WhenAll(Enumerable.Range(0, Workers).Select(async _ =>
            {
                for (var round = 0; round < Rounds; round++)
                {
                    await RunLoopAsync(host.Services, tenants, () => Interlocked.Increment(ref processed), ct);
                }
            }));
            await stopSampling.CancelAsync();
            var peak = await sampler;

            Assert.Equal(TenantCount * Workers * Rounds, processed);
            Assert.True(peak <= PeakBound, $"Peak of {peak} connections to {TenantCount} tenant databases (bound {PeakBound}).");

            var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
            var open = await CountConnectionsAsync(names, ct);
            while (open > IdleBound && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
                open = await CountConnectionsAsync(names, ct);
            }

            Assert.True(open <= IdleBound, $"{open} connections to {TenantCount} tenant databases still open after the loop.");
        }
        finally
        {
            foreach (var tenant in tenants)
            {
                await postgres.DropDatabaseAsync(tenant.DatabaseName, CancellationToken.None);
            }
        }
    }

    private Task RunLoopAsync(IServiceProvider services, IReadOnlyList<TenantId> tenants, Action done, CancellationToken ct) =>
        services.GetRequiredService<ITenantLoop>().ForEachAsync(tenants, async (tenant, token) =>
        {
            await using var scope = services.CreateAsyncScope();

            // Shape of the connection strings stored in the catalog before M11-00 (no idle settings, 20 per pool).
            var stored = new NpgsqlConnectionStringBuilder(postgres.ServerConnectionString)
            {
                Database = tenant.DatabaseName,
                MaxPoolSize = 20,
            }.ConnectionString;
            scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenant, stored);
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            await db.Database.ExecuteSqlRawAsync("SELECT pg_sleep(0.02)", token);
            done();
        }, ct);

    private async Task<long> SamplePeakAsync(string[] names, CancellationToken ct)
    {
        var peak = 0L;
        try
        {
            while (true)
            {
                peak = Math.Max(peak, await CountConnectionsAsync(names, ct));
                await Task.Delay(TimeSpan.FromMilliseconds(10), ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return peak;
        }
    }

    private async Task<long> CountConnectionsAsync(string[] names, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionStringFor("postgres"));
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM pg_stat_activity WHERE datname = ANY(@names)";
        command.Parameters.AddWithValue("names", names);
        return (long)(await command.ExecuteScalarAsync(ct))!;
    }
}
