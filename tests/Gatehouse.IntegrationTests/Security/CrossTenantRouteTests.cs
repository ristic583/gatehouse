using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Gatehouse.Application.Access.Dtos;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Security;

/// <summary>
/// Security regression (M11, area 1): every tenant route with an id, generated from the host's route table, answers a Bricks caller
/// that sends a Nordlys id with 404 (reads) or a refusal (writes). New routes are covered automatically; a route parameter without a
/// known Nordlys id fails <see cref="Every_guid_route_parameter_has_a_cross_tenant_id"/> so the case list cannot silently shrink.
/// </summary>
public sealed class CrossTenantRouteTests(PostgresFixture postgres) : IAsyncLifetime
{
    /// <summary>Bricks callers with different rights, so a 404 is not hidden behind a role 403 for every caller.</summary>
    private static readonly DemoUser[] BricksCallers = [DemoUser.Mia, DemoUser.Paul, DemoUser.Jan, DemoUser.Max];

    private readonly GatehouseApiFactory _api = new(postgres);

    public ValueTask InitializeAsync() => _api.InitializeAsync();

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    [Fact]
    public async Task Every_guid_route_parameter_has_a_cross_tenant_id()
    {
        var ids = await NordlysIdsAsync();

        var unknown = TenantRoutes().SelectMany(r => r.GuidParameters).Distinct().Where(p => !ids.ContainsKey(p)).ToList();

        Assert.True(unknown.Count == 0, "Add a Nordlys id for: " + string.Join(", ", unknown));
        Assert.True(TenantRoutes().Count >= 50, "The route table looks incomplete.");
    }

    [Fact]
    public async Task Read_routes_answer_404_for_other_tenant_ids()
    {
        var ids = await NordlysIdsAsync();
        var failures = new List<string>();
        foreach (var route in TenantRoutes().Where(r => r.IsRead && r.GuidParameters.Count > 0))
        {
            var statuses = await CallAsBricksAsync(route, route.Url(p => ids[p].ToString()));
            if (statuses.Any(s => (int)s is < 300 or >= 500) || !statuses.Contains(HttpStatusCode.NotFound))
            {
                failures.Add($"{route}: {string.Join(",", statuses.Select(s => (int)s))}");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public async Task Write_routes_refuse_other_tenant_ids()
    {
        var ids = await NordlysIdsAsync();
        var failures = new List<string>();
        foreach (var route in TenantRoutes().Where(r => !r.IsRead && r.GuidParameters.Count > 0))
        {
            var statuses = await CallAsBricksAsync(route, route.Url(p => ids[p].ToString()));
            if (statuses.Any(s => (int)s is < 400 or >= 500))
            {
                failures.Add($"{route}: {string.Join(",", statuses.Select(s => (int)s))}");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures));
        await AssertNordlysUnchangedAsync(ids);
    }

    [Fact]
    public async Task Ids_in_bodies_and_queries_of_other_tenant_are_refused()
    {
        var ct = TestContext.Current.CancellationToken;
        var ids = await NordlysIdsAsync();
        var now = DateTimeOffset.UtcNow;
        using var paul = _api.CreateClientFor(DemoUser.Paul);
        using var mia = _api.CreateClientFor(DemoUser.Mia);
        using var lena = _api.CreateClientFor(DemoUser.Lena);

        using var door = await paul.PostAsJsonAsync(Relative("/api/v1/live/door-commands"), new { kind = "LockdownStart", scope = "Door", scopeId = ids["doorId"], reason = "x" }, ct);
        using var floor = await paul.PostAsJsonAsync(Relative("/api/v1/live/door-commands"), new { kind = "LockdownStart", scope = "Floor", scopeId = ids["floorId"], reason = "x" }, ct);
        using var request = await lena.PostAsJsonAsync(Relative("/api/v1/access-requests"), new CreateAccessRequestDto(null, ids["areaId"], now.AddDays(1), now.AddDays(5), "x"), ct);
        using var role = await mia.PostAsJsonAsync(Relative("/api/v1/admin/role-assignments"), new { personId = ids["personId"], role = "SecurityStaff", siteId = (Guid?)null }, ct);
        var bricksArea = await Access.AccessRequestEndpointTests.AreaIdAsync(_api, "bricks", "BER-LAB");
        using var owners = await mia.PutAsJsonAsync(Relative($"/api/v1/admin/areas/{bricksArea}/owners"), new { personIds = new[] { ids["personId"] } }, ct);
        var alarms = await paul.GetStringAsync(Relative($"/api/v1/live/alarms?siteId={ids["siteId"]}"), ct);
        var reports = await paul.GetStringAsync(Relative($"/api/v1/reconciliation/reports?pacsInstanceId={ids["pacsInstanceId"]}"), ct);

        Assert.Equal(HttpStatusCode.NotFound, door.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, floor.StatusCode);
        Assert.True(request.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest, $"access request: {(int)request.StatusCode}");
        Assert.True(role.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest, $"role: {(int)role.StatusCode}");
        Assert.True(owners.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest, $"owners: {(int)owners.StatusCode}");
        Assert.Equal("[]", alarms);
        Assert.DoesNotContain(ids["reportId"].ToString(), reports, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Live_hub_refuses_joining_other_tenant_site()
    {
        var ct = TestContext.Current.CancellationToken;
        var ids = await NordlysIdsAsync();
        await using var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(_api.Server.BaseAddress, "api/v1/live/hub"), HttpTransportType.LongPolling, o =>
            {
                o.HttpMessageHandlerFactory = _ => _api.Server.CreateHandler();
                o.Headers[TestAuthHandler.SubHeader] = DemoUser.Paul.Subject;
                o.Headers[TestAuthHandler.EmailHeader] = DemoUser.Paul.Email;
                o.Headers[TestAuthHandler.TenantHeader] = DemoUser.Paul.Tenant;
            })
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();
        await hub.StartAsync(ct);

        var denied = await Assert.ThrowsAsync<HubException>(() => hub.InvokeAsync("JoinSite", ids["siteId"], ct));

        Assert.Contains("auth.forbidden", denied.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Photo_and_floor_drawing_objects_are_stored_under_the_tenant_prefix()
    {
        var ct = TestContext.Current.CancellationToken;
        using var lena = _api.CreateClientFor(DemoUser.Lena);
        using var mia = _api.CreateClientFor(DemoUser.Mia);
        var floorId = await Access.AccessMapEndpointTests.FloorIdAsync(_api, "bricks", "BER-OFF", 0);
        using var photoUpload = await SecurityUploads.UploadAsync(lena, "/api/v1/me/photo", SecurityUploads.Png(4, 4), ct);
        using var drawingUpload = await SecurityUploads.UploadAsync(mia, $"/api/v1/admin/floors/{floorId}/background", SecurityUploads.Png(64, 64), ct);

        Assert.True(photoUpload.IsSuccessStatusCode, $"photo {(int)photoUpload.StatusCode}");
        Assert.True(drawingUpload.IsSuccessStatusCode, $"drawing {(int)drawingUpload.StatusCode}");
        await using var scope = await _api.CreateTenantScopeAsync("bricks");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var photoKeys = await db.Photos.AsNoTracking().Select(p => p.ObjectKey).ToListAsync(ct);
        var drawingKeys = await db.Floors.AsNoTracking().Where(f => f.BackgroundObjectKey != null).Select(f => f.BackgroundObjectKey!).ToListAsync(ct);
        Assert.NotEmpty(photoKeys);
        Assert.NotEmpty(drawingKeys);
        Assert.All(photoKeys.Concat(drawingKeys), key => Assert.StartsWith("tenant/bricks/", key, StringComparison.Ordinal));
    }

    private List<ApiRoute> TenantRoutes() =>
        ApiRouteCatalog.Routes(_api.Services).Where(r => !r.Pattern.StartsWith("/api/v1/platform", StringComparison.Ordinal)).ToList();

    private async Task<List<HttpStatusCode>> CallAsBricksAsync(ApiRoute route, string url)
    {
        var statuses = new List<HttpStatusCode>();
        foreach (var caller in BricksCallers)
        {
            using var client = _api.CreateClientFor(caller);
            // A list body ("[]") is retried when "{}" cannot bind: Development rethrows bind errors as 500 (ThrowOnBadRequest).
            var status = await SendAsync(client, route, url, "{}");
            statuses.Add(status == HttpStatusCode.InternalServerError ? await SendAsync(client, route, url, "[]") : status);
        }

        return statuses;
    }

    private static async Task<HttpStatusCode> SendAsync(HttpClient client, ApiRoute route, string url, string body)
    {
        using var message = new HttpRequestMessage(new HttpMethod(route.Method), Relative(url));
        if (route.Method is "POST" or "PUT" or "PATCH")
        {
            message.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        using var response = await client.SendAsync(message, TestContext.Current.CancellationToken);
        return response.StatusCode;
    }

    /// <summary>The write sweep must not have changed Nordlys: the role assignment, card and pending request are still there.</summary>
    private async Task AssertNordlysUnchangedAsync(IReadOnlyDictionary<string, Guid> ids)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = await _api.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        Assert.True(await db.RoleAssignments.AnyAsync(r => r.Id == new Domain.People.RoleAssignmentId(ids["roleAssignmentId"]), ct));
        Assert.True(await db.Sites.AnyAsync(s => s.Id == new Domain.Sites.SiteId(ids["siteId"]), ct));
        Assert.True(await db.Doors.AnyAsync(d => d.Id == new Domain.Sites.DoorId(ids["doorId"]), ct));
        var person = await db.Persons.AsNoTracking().SingleAsync(p => p.Id == new Domain.People.PersonId(ids["personId"]), ct);
        Assert.Equal(Domain.People.PersonStatus.Active, person.Status);
    }

    /// <summary>One existing Nordlys id per route parameter name; rows that the seed does not have are created here.</summary>
    private async Task<IReadOnlyDictionary<string, Guid>> NordlysIdsAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        using var anna = _api.CreateClientFor(DemoUser.Anna);
        var requests = (await anna.GetFromJsonAsync<List<AccessRequestDto>>(Access.AccessRequestEndpointTests.Requests, ct))!;
        var pending = requests.First(r => r.Steps.Count > 0);

        await using var scope = await _api.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var now = DateTimeOffset.UtcNow;
        var annaPerson = await db.Persons.AsNoTracking().SingleAsync(p => p.Email == DemoUser.Anna.Email, ct);
        var door = await db.Doors.AsNoTracking().FirstAsync(ct);
        var pacs = await db.PacsInstances.AsNoTracking().FirstAsync(ct);
        var report = Domain.Reconciliation.ReconciliationReport.Request(pacs.Id, null, Domain.Fulfillment.CommandId.New(), now);
        var alarm = Domain.LiveBuilding.Alarm.Raise(door.Id, Domain.LiveBuilding.AlarmKind.Forced, Guid.CreateVersion7(), now);
        var task = Domain.Tasks.StaffTask.Open(Domain.Tasks.StaffTaskKind.FulfillmentFailed, null, "Fulfillment", Guid.CreateVersion7(), now);
        var card = await db.Cards.AsNoTracking().FirstOrDefaultAsync(ct);
        if (card is null)
        {
            card = Domain.Credentials.Card.Create(annaPerson.Id, "90000001", now).Value;
            db.Cards.Add(card);
        }

        db.ReconciliationReports.Add(report);
        db.Alarms.Add(alarm);
        db.StaffTasks.Add(task);
        await db.SaveChangesAsync(ct);

        return new Dictionary<string, Guid>(StringComparer.Ordinal)
        {
            ["siteId"] = (await db.Sites.AsNoTracking().FirstAsync(ct)).Id.Value,
            ["floorId"] = (await db.Floors.AsNoTracking().FirstAsync(ct)).Id.Value,
            ["areaId"] = (await db.Areas.AsNoTracking().FirstAsync(ct)).Id.Value,
            ["doorId"] = door.Id.Value,
            ["readerId"] = (await db.Readers.AsNoTracking().FirstAsync(ct)).Id.Value,
            ["personId"] = annaPerson.Id.Value,
            ["entitlementId"] = pending.EntitlementId,
            ["stepId"] = pending.Steps[0].StepId,
            ["fulfillmentId"] = (await db.Fulfillments.AsNoTracking().FirstAsync(ct)).Id.Value,
            ["taskId"] = task.Id.Value,
            ["reportId"] = report.Id.Value,
            ["alarmId"] = alarm.Id.Value,
            ["cardId"] = card.Id.Value,
            ["pacsInstanceId"] = pacs.Id.Value,
            ["roleAssignmentId"] = (await db.RoleAssignments.AsNoTracking().FirstAsync(ct)).Id.Value,
        };
    }

    private static Uri Relative(string url) => new(url, UriKind.Relative);
}
