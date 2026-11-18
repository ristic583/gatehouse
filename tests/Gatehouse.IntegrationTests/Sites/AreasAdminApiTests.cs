using System.Net;
using System.Net.Http.Json;
using Gatehouse.Application.Access.Dtos;
using Gatehouse.Application.LiveBuilding.Dtos;
using Gatehouse.Application.Sites.Dtos;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Sites;

/// <summary>authorization.md §11 area/door/reader admin routes, domain-model.md §2.1/§2.10 geometry rules.</summary>
public sealed class AreasAdminApiTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatehouseApiFactory _api = new(postgres);

    public ValueTask InitializeAsync() => _api.InitializeAsync();

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Create_area_with_owner_appears_on_access_map()
    {
        var ct = TestContext.Current.CancellationToken;
        using var freja = _api.CreateClientFor(DemoUser.Freja);
        var floor = await NewFloorAsync(freja, ct);
        var (owner, level) = await OwnerAndLevelAsync(DemoUser.Anna.Email, ct);

        var area = await CreateAreaAsync(freja, floor.FloorId, "Studio", "STU-01", Rect(100, 100, 300, 300), level, [owner], ct);

        Assert.Equal(owner, Assert.Single(area.Owners).PersonId);
        using var anna = _api.CreateClientFor(DemoUser.Anna);
        var map = (await anna.GetFromJsonAsync<FloorAccessMapDto>(new Uri($"/api/v1/floors/{floor.FloorId}/access-map", UriKind.Relative), ApiJson.Options, ct))!;
        Assert.Contains(map.Areas, a => a.AreaId == area.Id && a.Code == "STU-01");

        using var duplicateCode = await freja.PostAsJsonAsync(AreasUri(floor.FloorId), new SaveAreaRequest("Other", "STU-01", Rect(400, 400, 500, 500), level, [owner]), ApiJson.Options, ct);
        await AssertProblemAsync(duplicateCode, HttpStatusCode.Conflict, "area.code_taken", ct);
    }

    [Fact]
    public async Task Self_intersecting_polygon_is_400()
    {
        var ct = TestContext.Current.CancellationToken;
        using var freja = _api.CreateClientFor(DemoUser.Freja);
        var floor = await NewFloorAsync(freja, ct);
        var (owner, level) = await OwnerAndLevelAsync(DemoUser.Anna.Email, ct);
        PointDto[] bowtie = [new(100, 100), new(300, 300), new(300, 100), new(100, 300)];

        using var response = await freja.PostAsJsonAsync(AreasUri(floor.FloorId), new SaveAreaRequest("Bowtie", "BOW-01", bowtie, level, [owner]), ApiJson.Options, ct);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "area.invalid_polygon", ct);
    }

    [Fact]
    public async Task Overlapping_area_is_400()
    {
        var ct = TestContext.Current.CancellationToken;
        using var freja = _api.CreateClientFor(DemoUser.Freja);
        var floor = await NewFloorAsync(freja, ct);
        var (owner, level) = await OwnerAndLevelAsync(DemoUser.Anna.Email, ct);
        await CreateAreaAsync(freja, floor.FloorId, "West", "OVL-W", Rect(100, 100, 300, 300), level, [owner], ct);

        using var overlapping = await freja.PostAsJsonAsync(AreasUri(floor.FloorId), new SaveAreaRequest("East", "OVL-E", Rect(200, 100, 400, 300), level, [owner]), ApiJson.Options, ct);
        await AssertProblemAsync(overlapping, HttpStatusCode.BadRequest, "area.overlaps", ct);

        // Sharing a wall is fine.
        await CreateAreaAsync(freja, floor.FloorId, "East", "OVL-E", Rect(300, 100, 500, 300), level, [owner], ct);
    }

    [Fact]
    public async Task Area_with_entitlements_cannot_be_deleted()
    {
        var ct = TestContext.Current.CancellationToken;
        Guid areaId;
        await using (var scope = await _api.CreateTenantScopeAsync("nordlys"))
        {
            areaId = (await scope.ServiceProvider.GetRequiredService<TenantDbContext>().Entitlements.AsNoTracking().Select(e => e.AreaId).FirstAsync(ct)).Value;
        }

        using var freja = _api.CreateClientFor(DemoUser.Freja);
        using var response = await freja.DeleteAsync(new Uri($"/api/v1/admin/areas/{areaId}", UriKind.Relative), ct);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "area.in_use", ct);
    }

    [Fact]
    public async Task Owners_must_be_employees()
    {
        var ct = TestContext.Current.CancellationToken;
        using var freja = _api.CreateClientFor(DemoUser.Freja);
        var floor = await NewFloorAsync(freja, ct);
        var (owner, level) = await OwnerAndLevelAsync(DemoUser.Anna.Email, ct);
        var area = await CreateAreaAsync(freja, floor.FloorId, "Owned", "OWN-01", Rect(100, 100, 200, 200), level, [owner], ct);
        Guid nonEmployee;
        await using (var scope = await _api.CreateTenantScopeAsync("nordlys"))
        {
            nonEmployee = (await scope.ServiceProvider.GetRequiredService<TenantDbContext>().Persons.AsNoTracking()
                .Where(p => p.Type != PersonType.Employee).Select(p => p.Id).FirstAsync(ct)).Value;
        }

        using var external = await freja.PutAsJsonAsync(new Uri($"/api/v1/admin/areas/{area.Id}/owners", UriKind.Relative), new SetAreaOwnersRequest([nonEmployee]), ApiJson.Options, ct);
        await AssertProblemAsync(external, HttpStatusCode.BadRequest, "area.owner_not_employee", ct);
        using var empty = await freja.PutAsJsonAsync(new Uri($"/api/v1/admin/areas/{area.Id}/owners", UriKind.Relative), new SetAreaOwnersRequest([]), ApiJson.Options, ct);
        await AssertProblemAsync(empty, HttpStatusCode.Conflict, "area.last_owner", ct);
    }

    [Fact]
    public async Task Door_position_outside_plan_is_400()
    {
        var ct = TestContext.Current.CancellationToken;
        using var freja = _api.CreateClientFor(DemoUser.Freja);
        var floor = await NewFloorAsync(freja, ct);
        var (owner, level) = await OwnerAndLevelAsync(DemoUser.Anna.Email, ct);
        var area = await CreateAreaAsync(freja, floor.FloorId, "Lobby", "LOB-01", Rect(100, 100, 300, 300), level, [owner], ct);

        using var response = await freja.PostAsJsonAsync(DoorsUri(area.Id), new SaveDoorRequest("Ghost", new PointDto(5000, 50), null), ApiJson.Options, ct);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "door.invalid_position", ct);
    }

    [Fact]
    public async Task Door_on_shared_wall_moves_to_other_area()
    {
        var ct = TestContext.Current.CancellationToken;
        using var freja = _api.CreateClientFor(DemoUser.Freja);
        var floor = await NewFloorAsync(freja, ct);
        var other = await NewFloorAsync(freja, ct);
        var (owner, level) = await OwnerAndLevelAsync(DemoUser.Anna.Email, ct);
        var corridor = await CreateAreaAsync(freja, floor.FloorId, "Corridor", "COR-01", Rect(100, 100, 300, 300), level, [owner], ct);
        var room = await CreateAreaAsync(freja, floor.FloorId, "Room", "ROO-01", Rect(300, 100, 500, 300), level, [owner], ct);
        var elsewhere = await CreateAreaAsync(freja, other.FloorId, "Elsewhere", "ELS-01", Rect(100, 100, 300, 300), level, [owner], ct);
        using var created = await freja.PostAsJsonAsync(DoorsUri(corridor.Id), new SaveDoorRequest("Room door", new PointDto(300, 200), null), ApiJson.Options, ct);
        var door = (await created.Content.ReadFromJsonAsync<PlanDoorDto>(ApiJson.Options, ct))!;
        var uri = new Uri($"/api/v1/admin/doors/{door.Id}", UriKind.Relative);

        using var wrongFloor = await freja.PutAsJsonAsync(uri, new SaveDoorRequest("Room door", new PointDto(300, 200), null, elsewhere.Id), ApiJson.Options, ct);
        using var moved = await freja.PutAsJsonAsync(uri, new SaveDoorRequest("Room door", new PointDto(300, 200), null, room.Id), ApiJson.Options, ct);

        await AssertProblemAsync(wrongFloor, HttpStatusCode.BadRequest, "door.invalid_area", ct);
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        await using var scope = await _api.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        Assert.Equal(room.Id, (await db.Doors.AsNoTracking().SingleAsync(d => d.Id == new DoorId(door.Id), ct)).AreaId.Value);
    }

    [Fact]
    public async Task Area_without_access_control_cannot_be_requested()
    {
        var ct = TestContext.Current.CancellationToken;
        using var freja = _api.CreateClientFor(DemoUser.Freja);
        using var anna = _api.CreateClientFor(DemoUser.Anna);
        var floor = await NewFloorAsync(freja, ct);
        var (owner, level) = await OwnerAndLevelAsync(DemoUser.Mikkel.Email, ct);
        var corridor = await CreateAreaAsync(freja, floor.FloorId, "Public corridor", "PUB-01", Rect(100, 100, 300, 300), level, [owner], ct);

        using var update = await freja.PutAsJsonAsync(new Uri($"/api/v1/admin/areas/{corridor.Id}", UriKind.Relative),
            new SaveAreaRequest("Public corridor", "PUB-01", Rect(100, 100, 300, 300), level, null, false), ApiJson.Options, ct);
        var dto = (await update.Content.ReadFromJsonAsync<PlanAreaDto>(ApiJson.Options, ct))!;
        using var request = await Gatehouse.IntegrationTests.Access.AccessRequestEndpointTests.PostRequestAsync(_api, anna, "nordlys", "PUB-01");

        Assert.False(dto.AccessControlled);
        await AssertProblemAsync(request, HttpStatusCode.BadRequest, "request.area_not_access_controlled", ct);
    }

    [Fact]
    public async Task Duplicate_pacs_door_ref_is_409()
    {
        var ct = TestContext.Current.CancellationToken;
        using var freja = _api.CreateClientFor(DemoUser.Freja);
        var floor = await NewFloorAsync(freja, ct);
        var (owner, level) = await OwnerAndLevelAsync(DemoUser.Anna.Email, ct);
        var area = await CreateAreaAsync(freja, floor.FloorId, "Hall", "HAL-01", Rect(100, 100, 300, 300), level, [owner], ct);
        using var first = await freja.PostAsJsonAsync(DoorsUri(area.Id), new SaveDoorRequest("Hall A", new PointDto(100, 200), "PACS-D-77"), ApiJson.Options, ct);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var second = await freja.PostAsJsonAsync(DoorsUri(area.Id), new SaveDoorRequest("Hall B", new PointDto(300, 200), "PACS-D-77"), ApiJson.Options, ct);

        await AssertProblemAsync(second, HttpStatusCode.Conflict, "door.ref_taken", ct);
    }

    [Fact]
    public async Task Floor_plan_returns_doors_and_readers()
    {
        var ct = TestContext.Current.CancellationToken;
        using var freja = _api.CreateClientFor(DemoUser.Freja);
        var floor = await NewFloorAsync(freja, ct);
        var (owner, level) = await OwnerAndLevelAsync(DemoUser.Anna.Email, ct);
        var area = await CreateAreaAsync(freja, floor.FloorId, "Vault", "VLT-01", Rect(100, 100, 300, 300), level, [owner], ct);
        using var doorResponse = await freja.PostAsJsonAsync(DoorsUri(area.Id), new SaveDoorRequest("Vault door", new PointDto(200, 100), "VLT-D1"), ApiJson.Options, ct);
        var door = (await doorResponse.Content.ReadFromJsonAsync<PlanDoorDto>(ApiJson.Options, ct))!;
        using var readerResponse = await freja.PostAsJsonAsync(new Uri($"/api/v1/admin/doors/{door.Id}/readers", UriKind.Relative), new SaveReaderRequest("R-V1", ReaderDirection.In, new PointDto(200, 95), "VLT-R1"), ApiJson.Options, ct);
        Assert.Equal(HttpStatusCode.Created, readerResponse.StatusCode);

        var plan = (await freja.GetFromJsonAsync<FloorPlanDto>(new Uri($"/api/v1/admin/floors/{floor.FloorId}/plan", UriKind.Relative), ApiJson.Options, ct))!;

        var planArea = Assert.Single(plan.Areas);
        var planDoor = Assert.Single(planArea.Doors);
        Assert.Equal("VLT-D1", planDoor.PacsDoorRef);
        Assert.Equal(ReaderDirection.In, Assert.Single(planDoor.Readers).Direction);
        Assert.Equal(4, planArea.Polygon.Count);
    }

    [Fact]
    public async Task Edited_door_appears_in_live_floor()
    {
        var ct = TestContext.Current.CancellationToken;
        Guid floorId, doorId;
        Door door;
        await using (var scope = await _api.CreateTenantScopeAsync("nordlys"))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            door = await db.Doors.AsNoTracking().FirstAsync(ct);
            var area = await db.Areas.AsNoTracking().SingleAsync(a => a.Id == door.AreaId, ct);
            (floorId, doorId) = (area.FloorId.Value, door.Id.Value);
        }

        using var freja = _api.CreateClientFor(DemoUser.Freja);
        using var update = await freja.PutAsJsonAsync(new Uri($"/api/v1/admin/doors/{doorId}", UriKind.Relative),
            new SaveDoorRequest("Renamed entrance", new PointDto(door.Position.X, door.Position.Y), door.PacsDoorRef), ApiJson.Options, ct);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        using var jonas = _api.CreateClientFor(DemoUser.Jonas);
        var live = (await jonas.GetFromJsonAsync<LiveFloorDto>(new Uri($"/api/v1/live/floors/{floorId}", UriKind.Relative), ApiJson.Options, ct))!;

        Assert.Equal("Renamed entrance", Assert.Single(live.Doors, d => d.DoorId == doorId).Name);
    }

    private static Uri AreasUri(Guid floorId) => new($"/api/v1/admin/floors/{floorId}/areas", UriKind.Relative);

    private static Uri DoorsUri(Guid areaId) => new($"/api/v1/admin/areas/{areaId}/doors", UriKind.Relative);

    private static PointDto[] Rect(double x1, double y1, double x2, double y2) => [new(x1, y1), new(x2, y1), new(x2, y2), new(x1, y2)];

    private static async Task<AdminFloorDto> NewFloorAsync(HttpClient client, CancellationToken ct)
    {
        var code = "T" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        using var site = await client.PostAsJsonAsync(new Uri("/api/v1/admin/sites", UriKind.Relative), new SaveSiteRequest($"Site {code}", code, "Europe/Copenhagen"), ApiJson.Options, ct);
        Assert.Equal(HttpStatusCode.Created, site.StatusCode);
        var siteDto = (await site.Content.ReadFromJsonAsync<SiteDto>(ApiJson.Options, ct))!;
        using var floor = await client.PostAsJsonAsync(new Uri($"/api/v1/admin/sites/{siteDto.SiteId}/floors", UriKind.Relative), new SaveFloorRequest("Ground", 0, 1000, 800), ApiJson.Options, ct);
        Assert.Equal(HttpStatusCode.Created, floor.StatusCode);
        return (await floor.Content.ReadFromJsonAsync<AdminFloorDto>(ApiJson.Options, ct))!;
    }

    private async Task<(Guid Owner, Guid Level)> OwnerAndLevelAsync(string email, CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var owner = await db.Persons.AsNoTracking().Where(p => p.Email == email).Select(p => p.Id).SingleAsync(ct);
        var level = await db.SecurityLevels.AsNoTracking().OrderBy(l => l.Rank).Select(l => l.Id).FirstAsync(ct);
        return (owner.Value, level.Value);
    }

    private static async Task<PlanAreaDto> CreateAreaAsync(HttpClient client, Guid floorId, string name, string code, PointDto[] polygon, Guid level, Guid[] owners, CancellationToken ct)
    {
        using var response = await client.PostAsJsonAsync(AreasUri(floorId), new SaveAreaRequest(name, code, polygon, level, owners), ApiJson.Options, ct);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<PlanAreaDto>(ApiJson.Options, ct))!;
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code, CancellationToken ct)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Contains(code, await response.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }
}
