using System.Net;
using System.Net.Http.Json;
using Gatehouse.Application.Sites.Dtos;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.Sites;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Sites;

/// <summary>authorization.md §11 site/floor admin routes, domain-model.md §2.10 deletion and geometry rules.</summary>
public sealed class SitesAdminApiTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly Uri AdminSites = new("/api/v1/admin/sites", UriKind.Relative);
    private readonly GatehouseApiFactory _api = new(postgres);

    public ValueTask InitializeAsync() => _api.InitializeAsync();

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Create_site_and_floor_appear_in_sites_list()
    {
        var ct = TestContext.Current.CancellationToken;
        using var freja = _api.CreateClientFor(DemoUser.Freja);

        var site = await CreateSiteAsync(freja, "AAR-LAB", ct);
        using var floorResponse = await freja.PostAsJsonAsync(new Uri($"/api/v1/admin/sites/{site.SiteId}/floors", UriKind.Relative), new SaveFloorRequest("Lab floor", 1, 1200, 800), ApiJson.Options, ct);

        Assert.Equal(HttpStatusCode.Created, floorResponse.StatusCode);
        var floor = (await floorResponse.Content.ReadFromJsonAsync<AdminFloorDto>(ApiJson.Options, ct))!;
        using var anna = _api.CreateClientFor(DemoUser.Anna);
        var sites = (await anna.GetFromJsonAsync<List<SiteDto>>(new Uri("/api/v1/sites", UriKind.Relative), ApiJson.Options, ct))!;
        var listed = Assert.Single(sites, s => s.Code == "AAR-LAB");
        Assert.Equal(floor.FloorId, Assert.Single(listed.Floors).FloorId);

        using var duplicateLevel = await freja.PostAsJsonAsync(new Uri($"/api/v1/admin/sites/{site.SiteId}/floors", UriKind.Relative), new SaveFloorRequest("Again", 1, 1200, 800), ApiJson.Options, ct);
        await AssertProblemAsync(duplicateLevel, HttpStatusCode.Conflict, "floor.level_taken", ct);
    }

    [Fact]
    public async Task Duplicate_site_code_is_409()
    {
        var ct = TestContext.Current.CancellationToken;
        using var freja = _api.CreateClientFor(DemoUser.Freja);

        using var response = await freja.PostAsJsonAsync(AdminSites, new SaveSiteRequest("Another HQ", "CPH-HQ", "Europe/Copenhagen"), ApiJson.Options, ct);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "site.code_taken", ct);
    }

    [Fact]
    public async Task Delete_site_with_floors_is_409()
    {
        var ct = TestContext.Current.CancellationToken;
        using var freja = _api.CreateClientFor(DemoUser.Freja);
        var site = await CreateSiteAsync(freja, "ODE-OFF", ct);
        using var floor = await freja.PostAsJsonAsync(new Uri($"/api/v1/admin/sites/{site.SiteId}/floors", UriKind.Relative), new SaveFloorRequest("Ground", 0, 1000, 1000), ApiJson.Options, ct);
        var floorDto = (await floor.Content.ReadFromJsonAsync<AdminFloorDto>(ApiJson.Options, ct))!;

        using var blocked = await freja.DeleteAsync(new Uri($"/api/v1/admin/sites/{site.SiteId}", UriKind.Relative), ct);
        await AssertProblemAsync(blocked, HttpStatusCode.Conflict, "site.not_empty", ct);

        using var deleteFloor = await freja.DeleteAsync(new Uri($"/api/v1/admin/floors/{floorDto.FloorId}", UriKind.Relative), ct);
        Assert.Equal(HttpStatusCode.NoContent, deleteFloor.StatusCode);
        using var deleteSite = await freja.DeleteAsync(new Uri($"/api/v1/admin/sites/{site.SiteId}", UriKind.Relative), ct);
        Assert.Equal(HttpStatusCode.NoContent, deleteSite.StatusCode);
    }

    [Fact]
    public async Task Delete_site_covered_by_pacs_is_409()
    {
        var ct = TestContext.Current.CancellationToken;
        using var freja = _api.CreateClientFor(DemoUser.Freja);
        var site = await CreateSiteAsync(freja, "VEJ-WH", ct);
        await using (var scope = await _api.CreateTenantScopeAsync("nordlys"))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            db.PacsInstances.Add(PacsInstance.Create($"Warehouse PACS {Guid.NewGuid():N}", PacsVendor.LegacyPacs, [new SiteId(site.SiteId)]).Value);
            await db.SaveChangesAsync(ct);
        }

        using var response = await freja.DeleteAsync(new Uri($"/api/v1/admin/sites/{site.SiteId}", UriKind.Relative), ct);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "site.not_empty", ct);
    }

    [Fact]
    public async Task Shrinking_floor_below_area_is_400()
    {
        var ct = TestContext.Current.CancellationToken;
        Floor floor;
        await using (var scope = await _api.CreateTenantScopeAsync("nordlys"))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var floorId = await db.Areas.AsNoTracking().Select(a => a.FloorId).FirstAsync(ct);
            floor = await db.Floors.AsNoTracking().SingleAsync(f => f.Id == floorId, ct);
        }

        using var freja = _api.CreateClientFor(DemoUser.Freja);
        using var response = await freja.PutAsJsonAsync(new Uri($"/api/v1/admin/floors/{floor.Id.Value}", UriKind.Relative), new SaveFloorRequest(floor.Name, floor.Level, 100, 100), ApiJson.Options, ct);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "floor.invalid_plan_size", ct);
        using var rename = await freja.PutAsJsonAsync(new Uri($"/api/v1/admin/floors/{floor.Id.Value}", UriKind.Relative), new SaveFloorRequest(floor.Name + " (renamed)", floor.Level, floor.PlanWidth, floor.PlanHeight), ApiJson.Options, ct);
        Assert.Equal(HttpStatusCode.OK, rename.StatusCode);
        using var deleteWithAreas = await freja.DeleteAsync(new Uri($"/api/v1/admin/floors/{floor.Id.Value}", UriKind.Relative), ct);
        await AssertProblemAsync(deleteWithAreas, HttpStatusCode.Conflict, "floor.not_empty", ct);
    }

    [Fact]
    public async Task Non_admin_is_403()
    {
        var ct = TestContext.Current.CancellationToken;
        using var anna = _api.CreateClientFor(DemoUser.Anna);

        using var create = await anna.PostAsJsonAsync(AdminSites, new SaveSiteRequest("Nope", "NOPE", "Europe/Copenhagen"), ApiJson.Options, ct);
        using var levels = await anna.GetAsync(new Uri("/api/v1/admin/security-levels", UriKind.Relative), ct);

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, levels.StatusCode);
    }

    [Fact]
    public async Task Changes_are_audited()
    {
        var ct = TestContext.Current.CancellationToken;
        using var freja = _api.CreateClientFor(DemoUser.Freja);
        var site = await CreateSiteAsync(freja, "AUD-IT", ct);
        using var update = await freja.PutAsJsonAsync(new Uri($"/api/v1/admin/sites/{site.SiteId}", UriKind.Relative), new SaveSiteRequest("Audit office", "AUD-IT", "Europe/Berlin"), ApiJson.Options, ct);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        using var delete = await freja.DeleteAsync(new Uri($"/api/v1/admin/sites/{site.SiteId}", UriKind.Relative), ct);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        var levels = (await freja.GetFromJsonAsync<List<SecurityLevelAdminDto>>(new Uri("/api/v1/admin/security-levels", UriKind.Relative), ApiJson.Options, ct))!;
        Assert.Equal([1, 2, 3, 4], levels.Select(l => l.Rank));

        await using var scope = await _api.CreateTenantScopeAsync("nordlys");
        var subject = site.SiteId.ToString();
        var actions = await scope.ServiceProvider.GetRequiredService<TenantDbContext>().AuditEntries.AsNoTracking()
            .Where(e => e.SubjectId == subject).OrderBy(e => e.Sequence).Select(e => e.Action).ToListAsync(ct);
        Assert.Equal(["site.created", "site.updated", "site.deleted"], actions);
    }

    private static async Task<SiteDto> CreateSiteAsync(HttpClient client, string code, CancellationToken ct)
    {
        using var response = await client.PostAsJsonAsync(AdminSites, new SaveSiteRequest($"Site {code}", code, "Europe/Copenhagen"), ApiJson.Options, ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<SiteDto>(ApiJson.Options, ct))!;
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code, CancellationToken ct)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Contains(code, await response.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }
}
