using Gatehouse.Application.Tenancy;
using Gatehouse.Infrastructure.LiveBuilding;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Gatehouse.IntegrationTests.LiveBuilding;

public sealed class DoorEventPartitionMaintenanceTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Tenant = "nordlys";

    private readonly GatehouseApiFactory _api = new(postgres);

    public ValueTask InitializeAsync() => _api.InitializeAsync();

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    [Fact]
    public async Task Creates_week_ahead_and_drops_expired()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var expired = today.AddDays(-(DoorEventPartitionMaintenance.DefaultRetentionDays + 10));
        await using (var scope = await _api.CreateTenantScopeAsync(Tenant))
        {
            await DoorEventPartitions.EnsureAsync(scope.ServiceProvider.GetRequiredService<TenantDbContext>(), expired, 1, ct);
        }

        using var services = _api.Services.CreateScope();
        var maintenance = new DoorEventPartitionMaintenance(
            services.ServiceProvider.GetRequiredService<ICatalogReader>(),
            services.ServiceProvider.GetRequiredService<ITenantScopeFactory>(),
            services.ServiceProvider.GetRequiredService<ITenantLoop>(),
            new FakeTimeProvider(now),
            new ConfigurationBuilder().Build(),
            NullLogger<DoorEventPartitionMaintenance>.Instance);

        var dropped = await maintenance.RunOnceAsync(ct);

        Assert.True(dropped[Tenant] >= 1);
        await using var check = await _api.CreateTenantScopeAsync(Tenant);
        var partitions = await check.ServiceProvider.GetRequiredService<TenantDbContext>().Database.SqlQueryRaw<string>(
            """
            SELECT c.relname AS "Value" FROM pg_inherits i JOIN pg_class c ON c.oid = i.inhrelid
            JOIN pg_class p ON p.oid = i.inhparent WHERE p.relname = 'door_events'
            """).ToListAsync(ct);
        for (var i = 0; i < DoorEventPartitionMaintenance.DaysAhead; i++)
        {
            Assert.Contains(DoorEventPartitions.NameFor(today.AddDays(i)), partitions);
        }

        Assert.DoesNotContain(DoorEventPartitions.NameFor(expired), partitions);
    }
}
