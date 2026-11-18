using System.Net;
using System.Net.Http.Json;
using Gatehouse.Application.LiveBuilding.RequestDoorCommand;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.LiveBuilding;

public sealed class DoorCommandTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Tenant = "nordlys";
    private const string Instance = "HQ-Copenhagen";
    private static readonly Uri Endpoint = new("/api/v1/live/door-commands", UriKind.Relative);

    private readonly GatehouseApiFactory _api = new(postgres);
    private FulfillmentMode _mode;

    public async ValueTask InitializeAsync()
    {
        await _api.InitializeAsync();
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        _mode = (await db.PacsInstances.AsNoTracking().SingleAsync(p => p.Name == Instance)).FulfillmentMode;
        await db.PacsInstances.Where(p => p.Name == Instance).ExecuteUpdateAsync(s => s.SetProperty(p => p.FulfillmentMode, FulfillmentMode.Connector));
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    [Fact]
    public async Task Lockdown_floor_creates_high_priority_command_per_connector_door()
    {
        var ct = TestContext.Current.CancellationToken;
        var (floorId, _) = await FloorAndDoorAsync();
        using var jonas = _api.CreateClientFor(DemoUser.Jonas);

        using var response = await jonas.PostAsJsonAsync(Endpoint, new { kind = "LockdownStart", scope = "Floor", scopeId = floorId, reason = "Drill" }, ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<DoorCommandResultDto>(ApiJson.Options, ct);
        Assert.NotNull(result);
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var expected = await (
                from d in db.Doors
                join a in db.Areas on d.AreaId equals a.Id
                where a.FloorId == new Domain.Sites.FloorId(floorId) && d.PacsDoorRef != null
                select d.Id)
            .CountAsync(ct);
        Assert.True(expected > 0);
        Assert.Equal(expected, result.DoorCount);
        var key = $":{result.DoorCommandId}:";
        var commands = await db.ConnectorCommands.AsNoTracking().Where(c => c.IdempotencyKey.Contains(key)).ToListAsync(ct);
        Assert.Equal(expected, commands.Count);
        Assert.All(commands, c =>
        {
            Assert.Equal(CommandPriority.High, c.Priority);
            Assert.Equal("Lockdown", c.Type);
            Assert.StartsWith("door:", c.PartitionKey, StringComparison.Ordinal);
            Assert.Null(c.FulfillmentId);
            Assert.Contains("Start", c.PayloadJson, StringComparison.Ordinal);
        });
        Assert.Equal(expected, commands.Select(c => c.PartitionKey).Distinct().Count());
        var stored = await db.DoorCommands.AsNoTracking().SingleAsync(c => c.Id == new Domain.LiveBuilding.DoorCommandId(result.DoorCommandId), ct);
        Assert.Equal(expected, stored.DoorCount);
    }

    [Fact]
    public async Task Momentary_unlock_requires_door_scope_and_duration()
    {
        var ct = TestContext.Current.CancellationToken;
        var (floorId, doorId) = await FloorAndDoorAsync();
        using var jonas = _api.CreateClientFor(DemoUser.Jonas);

        using var floorScope = await jonas.PostAsJsonAsync(Endpoint, new { kind = "MomentaryUnlock", scope = "Floor", scopeId = floorId, reason = "Visitor", durationSeconds = 5 }, ct);
        using var noDuration = await jonas.PostAsJsonAsync(Endpoint, new { kind = "MomentaryUnlock", scope = "Door", scopeId = doorId, reason = "Visitor" }, ct);
        using var ok = await jonas.PostAsJsonAsync(Endpoint, new { kind = "MomentaryUnlock", scope = "Door", scopeId = doorId, reason = "Visitor", durationSeconds = 5 }, ct);

        Assert.Equal(HttpStatusCode.BadRequest, floorScope.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noDuration.StatusCode);
        Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        var result = await ok.Content.ReadFromJsonAsync<DoorCommandResultDto>(ApiJson.Options, ct);
        Assert.NotNull(result);
        Assert.Equal(1, result.DoorCount);
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var command = await db.ConnectorCommands.AsNoTracking().SingleAsync(c => c.IdempotencyKey == $"MomentaryUnlock:{result.DoorCommandId}:{doorId}", ct);
        Assert.Equal($"door:{doorId}", command.PartitionKey);
        Assert.Contains("D-CPH-VAULT", command.PayloadJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tenant_admin_is_forbidden()
    {
        var ct = TestContext.Current.CancellationToken;
        var (floorId, _) = await FloorAndDoorAsync();
        using var freja = _api.CreateClientFor(DemoUser.Freja);

        using var response = await freja.PostAsJsonAsync(Endpoint, new { kind = "LockdownStart", scope = "Floor", scopeId = floorId, reason = "Drill" }, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Manual_only_scope_is_409()
    {
        var ct = TestContext.Current.CancellationToken;
        Guid siteId;
        await using (var scope = await _api.CreateTenantScopeAsync(Tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            siteId = (await db.Sites.AsNoTracking().SingleAsync(s => s.Code == "AAR-WH", ct)).Id.Value;
        }

        using var jonas = _api.CreateClientFor(DemoUser.Jonas);

        using var response = await jonas.PostAsJsonAsync(Endpoint, new { kind = "LockdownStart", scope = "Site", scopeId = siteId, reason = "Drill" }, ct);
        using var missing = await jonas.PostAsJsonAsync(Endpoint, new { kind = "LockdownStart", scope = "Area", scopeId = Guid.CreateVersion7(), reason = "Drill" }, ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("door_command.no_connector_doors", await response.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Is_audited_with_reason()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, doorId) = await FloorAndDoorAsync();
        using var jonas = _api.CreateClientFor(DemoUser.Jonas);

        using var response = await jonas.PostAsJsonAsync(Endpoint, new { kind = "LockdownClear", scope = "Door", scopeId = doorId, reason = "All clear after drill" }, ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<DoorCommandResultDto>(ApiJson.Options, ct);
        Assert.NotNull(result);
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var entry = await db.AuditEntries.AsNoTracking()
            .SingleAsync(a => a.Action == "door.command_requested" && a.SubjectId == result.DoorCommandId.ToString(), ct);
        Assert.Equal("DoorCommand", entry.SubjectType);
        Assert.Contains("All clear after drill", entry.Payload, StringComparison.Ordinal);
        Assert.Contains("LockdownClear", entry.Payload, StringComparison.Ordinal);
    }

    private async Task<(Guid FloorId, Guid DoorId)> FloorAndDoorAsync()
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var ct = TestContext.Current.CancellationToken;
        var area = await db.Areas.AsNoTracking().SingleAsync(a => a.Code == "CPH-VAULT", ct);
        var door = await db.Doors.AsNoTracking().Where(d => d.AreaId == area.Id && d.PacsDoorRef == "D-CPH-VAULT").Select(d => d.Id).SingleAsync(ct);
        return (area.FloorId.Value, door.Value);
    }
}
