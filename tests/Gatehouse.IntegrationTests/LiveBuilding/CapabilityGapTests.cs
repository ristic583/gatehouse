using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gatehouse.Application.Connectors;
using Gatehouse.Application.Fulfillment.Dtos;
using Gatehouse.Application.LiveBuilding.Dtos;
using Gatehouse.Application.LiveBuilding.RequestDoorCommand;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Adapters;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.Sites;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.LiveBuilding;

public sealed class CapabilityGapTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Tenant = "nordlys";
    private static readonly Uri DoorCommands = new("/api/v1/live/door-commands", UriKind.Relative);
    private static readonly string CloudCaps = JsonSerializer.Serialize(
        new PacsCapabilities("CloudPacs", "1.0.0", false, true, true, true, true, [DoorCommandKind.MomentaryUnlock], 100), ConnectorJson.Options);
    private static readonly string LegacyCaps = JsonSerializer.Serialize(
        new PacsCapabilities("LegacyPacs", "1.0.0", true, false, false, false, true, [DoorCommandKind.MomentaryUnlock, DoorCommandKind.Lockdown], 50), ConnectorJson.Options);

    private readonly GatehouseApiFactory _api = new(postgres);
    private readonly List<(PacsInstanceId Id, FulfillmentMode Mode, DateTimeOffset? Heartbeat, string? Caps)> _saved = [];
    private PacsInstanceId _aarhus;
    private PacsInstanceId _hq;
    private (AccessLevelMappingId Id, PacsInstanceId Original)? _remapped;
    private FulfillmentId? _fulfillment;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        await _api.InitializeAsync();
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        foreach (var instance in await db.PacsInstances.Where(p => p.Name == "Warehouse-Aarhus" || p.Name == "HQ-Copenhagen").ToListAsync(Ct))
        {
            _saved.Add((instance.Id, instance.FulfillmentMode, instance.LastHeartbeatAt, instance.CapabilitiesJson));
            instance.SetFulfillmentMode(FulfillmentMode.Connector);
            instance.RecordHeartbeat(DateTimeOffset.UtcNow, "1.0.0-test", instance.Name == "Warehouse-Aarhus" ? CloudCaps : LegacyCaps);
            (instance.Name == "Warehouse-Aarhus" ? ref _aarhus : ref _hq) = instance.Id;
        }

        await db.SaveChangesAsync(Ct);
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    [Fact]
    public async Task Connector_health_lists_capability_gaps()
    {
        var siteId = await SiteIdAsync("AAR-WH");
        using var jonas = _api.CreateClientFor(DemoUser.Jonas);

        var list = await jonas.GetFromJsonAsync<List<ConnectorHealthDto>>(new Uri($"/api/v1/live/connectors?siteId={siteId}", UriKind.Relative), ApiJson.Options, Ct);

        var aarhus = Assert.Single(list!, c => c.PacsInstanceId == _aarhus.Value);
        Assert.Equal([PacsCapabilityGaps.NativeValidity, PacsCapabilityGaps.Lockdown], aarhus.CapabilityGaps);
    }

    [Fact]
    public async Task Fulfillment_detail_lists_capability_gaps()
    {
        await using (var scope = await _api.CreateTenantScopeAsync(Tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var used = await db.Fulfillments.AsNoTracking().Where(f => f.Kind == FulfillmentKind.Revoke).Select(f => f.EntitlementId).ToListAsync(Ct);
            var entitlement = await db.Entitlements.AsNoTracking().FirstAsync(e => !used.Contains(e.Id), Ct);
            var fulfillment = Gatehouse.Domain.Fulfillment.Fulfillment.Create(entitlement.Id, _aarhus, FulfillmentKind.Revoke, CommandPriority.Normal, DateTimeOffset.UtcNow);
            db.Fulfillments.Add(fulfillment);
            await db.SaveChangesAsync(Ct);
            _fulfillment = fulfillment.Id;
        }

        using var jonas = _api.CreateClientFor(DemoUser.Jonas);
        var detail = await jonas.GetFromJsonAsync<FulfillmentDetailDto>(new Uri($"/api/v1/fulfillments/{_fulfillment.Value.Value}", UriKind.Relative), ApiJson.Options, Ct);

        Assert.True(detail!.ConnectorManaged);
        Assert.Equal([PacsCapabilityGaps.NativeValidity, PacsCapabilityGaps.Lockdown], detail.CapabilityGaps);
    }

    [Fact]
    public async Task Live_floor_door_cannot_lockdown_on_cloudpacs()
    {
        var floorId = await FloorIdAsync("AAR-WH");
        using var jonas = _api.CreateClientFor(DemoUser.Jonas);

        var floor = await jonas.GetFromJsonAsync<LiveFloorDto>(new Uri($"/api/v1/live/floors/{floorId}", UriKind.Relative), ApiJson.Options, Ct);

        var dock = Assert.Single(floor!.Doors, d => d.PacsDoorRef == "D-AAR-DOCK");
        Assert.True(dock.ConnectorManaged);
        Assert.True(dock.CanUnlock);
        Assert.False(dock.CanLockdown);
    }

    [Fact]
    public async Task Lockdown_on_cloudpacs_only_scope_is_409_unsupported()
    {
        var floorId = await FloorIdAsync("AAR-WH");
        using var jonas = _api.CreateClientFor(DemoUser.Jonas);

        using var response = await jonas.PostAsJsonAsync(DoorCommands, new { kind = "LockdownStart", scope = "Floor", scopeId = floorId, reason = "Drill" }, Ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("door_command.unsupported_by_pacs", problem.GetProperty("code").GetString());
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        Assert.False(await scope.ServiceProvider.GetRequiredService<TenantDbContext>().DoorCommands.AnyAsync(Ct));
    }

    [Fact]
    public async Task Lockdown_site_wide_skips_cloudpacs_doors()
    {
        var siteId = await SiteIdAsync("CPH-HQ");
        DoorId labDoor;
        await using (var scope = await _api.CreateTenantScopeAsync(Tenant))
        {
            // a CPH area handled by the CloudPACS instance (mixed PACS on one site)
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var lab = await db.Areas.AsNoTracking().SingleAsync(a => a.Code == "CPH-LAB", Ct);
            var mapping = await db.AccessLevelMappings.AsNoTracking().SingleAsync(m => m.AreaId == lab.Id, Ct);
            _remapped = (mapping.Id, mapping.PacsInstanceId);
            await db.AccessLevelMappings.Where(m => m.Id == mapping.Id).ExecuteUpdateAsync(u => u.SetProperty(m => m.PacsInstanceId, _aarhus), Ct);
            labDoor = (await db.Doors.AsNoTracking().SingleAsync(d => d.AreaId == lab.Id, Ct)).Id;
        }

        using var jonas = _api.CreateClientFor(DemoUser.Jonas);
        using var response = await jonas.PostAsJsonAsync(DoorCommands, new { kind = "LockdownStart", scope = "Site", scopeId = siteId, reason = "Drill" }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<DoorCommandResultDto>(ApiJson.Options, Ct);
        Assert.True(result!.DoorCount > 0);
        Assert.True(result.SkippedDoorCount >= 1);
        await using var check = await _api.CreateTenantScopeAsync(Tenant);
        var commands = await check.ServiceProvider.GetRequiredService<TenantDbContext>().ConnectorCommands.AsNoTracking()
            .Where(c => c.IdempotencyKey.Contains($":{result.DoorCommandId}:")).ToListAsync(Ct);
        Assert.Equal(result.DoorCount, commands.Count);
        Assert.All(commands, c => Assert.Equal(_hq, c.PacsInstanceId));
        Assert.DoesNotContain(commands, c => c.PartitionKey == $"door:{labDoor.Value}");
    }

    private async Task<Guid> SiteIdAsync(string code)
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        return (await scope.ServiceProvider.GetRequiredService<TenantDbContext>().Sites.AsNoTracking().SingleAsync(s => s.Code == code, Ct)).Id.Value;
    }

    private async Task<Guid> FloorIdAsync(string siteCode)
    {
        var siteId = new SiteId(await SiteIdAsync(siteCode));
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        return (await scope.ServiceProvider.GetRequiredService<TenantDbContext>().Floors.AsNoTracking().FirstAsync(f => f.SiteId == siteId, Ct)).Id.Value;
    }
}
