using Gatehouse.Application.Fulfillment;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.Tenancy;
using Gatehouse.Infrastructure.Fulfillment;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Gatehouse.IntegrationTests.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Fulfillment;

public sealed class ConnectorHealthReaderTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Manual_instance_is_online() =>
        Assert.True((await ReadAsync(FulfillmentMode.Manual, null)).Online);

    [Fact]
    public async Task Connector_instance_without_heartbeat_is_offline() =>
        Assert.False((await ReadAsync(FulfillmentMode.Connector, null)).Online);

    [Fact]
    public async Task Connector_instance_with_recent_heartbeat_is_online() =>
        Assert.True((await ReadAsync(FulfillmentMode.Connector, Now.AddSeconds(-30))).Online);

    private async Task<ConnectorHealth> ReadAsync(FulfillmentMode mode, DateTimeOffset? heartbeat)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var host = await TenancyTestHost.CreateAsync(postgres, ct);
        var tenant = await host.AddTenantAsync("health-" + Guid.NewGuid().ToString("N")[..8], TenantStatus.Active, ct);
        await using var scope = await host.Services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(tenant, ct);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var pacs = PacsInstance.Create("P", PacsVendor.LegacyPacs, []).Value;
        pacs.SetFulfillmentMode(mode);
        if (heartbeat is { } at)
        {
            pacs.RecordHeartbeat(at, "1.0", null);
        }

        db.PacsInstances.Add(pacs);
        await db.SaveChangesAsync(ct);
        var reader = new PacsInstanceConnectorHealthReader(db, new FixedTime());
        return (await reader.GetAsync([pacs.Id], ct))[pacs.Id];
    }

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
