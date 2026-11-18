using System.Net;
using System.Net.Http.Json;
using Gatehouse.Application.Sites.Dtos;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Tenancy;

/// <summary>Every read endpoint: tenant A's ids are 404 for tenant B (CLAUDE.md tenancy rule 6).</summary>
public sealed class TenantIsolationTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatehouseApiFactory _factory = new(postgres);

    public ValueTask InitializeAsync() => _factory.InitializeAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Bricks_user_sees_no_nordlys_sites()
    {
        using var client = _factory.CreateClientFor(DemoUser.Lena);

        var sites = await client.GetFromJsonAsync<List<SiteDto>>(new Uri("/api/v1/sites", UriKind.Relative), TestContext.Current.CancellationToken);

        Assert.NotNull(sites);
        Assert.Equal("BER-OFF", Assert.Single(sites).Code);
    }

    [Fact]
    public async Task Nordlys_area_id_is_404_for_bricks_user()
    {
        var areaId = await NordlysAreaIdAsync("CPH-SERVER");
        await AssertNotFoundForBricksAsync($"/api/v1/areas/{areaId}");
    }

    [Fact]
    public async Task Nordlys_area_roster_is_404_for_bricks_staff()
    {
        var areaId = await NordlysAreaIdAsync("CPH-SERVER");
        await AssertNotFoundForClientAsync(DemoUser.Paul, $"/api/v1/areas/{areaId}/roster");
    }

    [Fact]
    public async Task Bricks_staff_sees_no_nordlys_tasks()
    {
        using var anna = _factory.CreateClientFor(DemoUser.Anna);
        using var mikkel = _factory.CreateClientFor(DemoUser.Mikkel);
        using var sofie = _factory.CreateClientFor(DemoUser.Sofie);
        using var jonas = _factory.CreateClientFor(DemoUser.Jonas);
        using var created = await Access.AccessRequestEndpointTests.PostRequestAsync(_factory, anna, "nordlys", "CPH-SERVER");
        var request = await Access.AccessRequestEndpointTests.CreatedAsync(created);
        (await Access.ApprovalEndpointTests.DecideAsync(mikkel, request.Steps[0].StepId, "approve")).Dispose();
        (await Access.ApprovalEndpointTests.DecideAsync(sofie, request.Steps[1].StepId, "approve")).Dispose();
        var queue = await Fulfillment.FulfillmentQueueEndpointTests.ListAsync(jonas, "open");
        var fid = queue.First(i => i.EntitlementId == request.EntitlementId).FulfillmentId;
        (await jonas.PostAsJsonAsync(new Uri($"/api/v1/fulfillments/{fid}/fail", UriKind.Relative), new { reason = "x" }, TestContext.Current.CancellationToken)).Dispose();
        await _factory.DrainMessagesAsync("nordlys");
        Assert.NotEmpty((await jonas.GetFromJsonAsync<List<Gatehouse.Application.Tasks.Dtos.StaffTaskDto>>(new Uri("/api/v1/tasks", UriKind.Relative), TestContext.Current.CancellationToken))!);

        using var paul = _factory.CreateClientFor(DemoUser.Paul);
        var tasks = await paul.GetFromJsonAsync<List<Gatehouse.Application.Tasks.Dtos.StaffTaskDto>>(new Uri("/api/v1/tasks", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Empty(tasks!);
    }

    [Fact]
    public async Task Nordlys_floor_access_map_is_404_for_bricks_user()
    {
        var floorId = await Access.AccessMapEndpointTests.FloorIdAsync(_factory, "nordlys", "CPH-HQ", 2);
        await AssertNotFoundForBricksAsync($"/api/v1/floors/{floorId}/access-map");
    }

    [Fact]
    public async Task Nordlys_request_id_is_404_for_bricks_user()
    {
        using var anna = _factory.CreateClientFor(DemoUser.Anna);
        using var response = await Access.AccessRequestEndpointTests.PostRequestAsync(_factory, anna, "nordlys", "CPH-SERVER");
        var dto = await Access.AccessRequestEndpointTests.CreatedAsync(response);

        await AssertNotFoundForBricksAsync($"/api/v1/access-requests/{dto.EntitlementId}");
    }

    [Fact]
    public async Task Nordlys_audit_entries_are_not_visible_to_bricks_admin()
    {
        var subject = Guid.NewGuid().ToString();
        await Audit.AuditEndpointTests.RecordAsync(_factory, "nordlys", subject);
        using var mia = _factory.CreateClientFor(DemoUser.Mia);
        var list = await mia.GetFromJsonAsync<List<Gatehouse.Application.Audit.Dtos.AuditEntryDto>>(new Uri($"/api/v1/audit/entries?subjectId={subject}", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Empty(list!);
    }

    [Fact]
    public async Task Reconciliation_reports_of_other_tenant_are_404()
    {
        var ct = TestContext.Current.CancellationToken;
        Domain.Reconciliation.ReconciliationReportId reportId;
        await using (var scope = await _factory.CreateTenantScopeAsync("nordlys"))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var instanceId = await db.PacsInstances.AsNoTracking().Select(p => p.Id).FirstAsync(ct);
            var report = Domain.Reconciliation.ReconciliationReport.Request(instanceId, null, Domain.Fulfillment.CommandId.New(), DateTimeOffset.UtcNow);
            report.Fail("connector.failed", "Test", DateTimeOffset.UtcNow);
            db.ReconciliationReports.Add(report);
            await db.SaveChangesAsync(ct);
            reportId = report.Id;
        }

        try
        {
            using var paul = _factory.CreateClientFor(DemoUser.Paul);
            using var detail = await paul.GetAsync(new Uri($"/api/v1/reconciliation/reports/{reportId.Value}", UriKind.Relative), ct);
            var list = await paul.GetFromJsonAsync<List<Gatehouse.Application.Reconciliation.Dtos.ReconciliationReportSummaryDto>>(
                new Uri("/api/v1/reconciliation/reports?limit=50", UriKind.Relative), ApiJson.Options, ct);

            Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);
            Assert.DoesNotContain(list!, r => r.ReportId == reportId.Value);
        }
        finally
        {
            await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            await db.ReconciliationReports.Where(r => r.Id == reportId).ExecuteDeleteAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Reconciliation_instances_are_tenant_scoped()
    {
        var ct = TestContext.Current.CancellationToken;
        List<Guid> nordlysInstances;
        await using (var scope = await _factory.CreateTenantScopeAsync("nordlys"))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            nordlysInstances = (await db.PacsInstances.AsNoTracking().ToListAsync(ct)).Select(p => p.Id.Value).ToList();
        }

        using var paul = _factory.CreateClientFor(DemoUser.Paul);
        var instances = await paul.GetFromJsonAsync<List<Gatehouse.Application.Reconciliation.Dtos.ReconciliationInstanceDto>>(
            new Uri("/api/v1/reconciliation/instances", UriKind.Relative), ApiJson.Options, ct);

        Assert.DoesNotContain(instances!, i => nordlysInstances.Contains(i.PacsInstanceId));
    }

    [Fact]
    public async Task Live_connectors_are_tenant_scoped()
    {
        var ct = TestContext.Current.CancellationToken;
        Guid hqSite;
        List<Guid> nordlysInstances;
        await using (var scope = await _factory.CreateTenantScopeAsync("nordlys"))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            hqSite = (await db.Sites.AsNoTracking().SingleAsync(s => s.Code == "CPH-HQ", ct)).Id.Value;
            nordlysInstances = (await db.PacsInstances.AsNoTracking().ToListAsync(ct)).Select(p => p.Id.Value).ToList();
        }

        using var paul = _factory.CreateClientFor(DemoUser.Paul);
        var all = await paul.GetFromJsonAsync<List<Gatehouse.Application.LiveBuilding.Dtos.ConnectorHealthDto>>(new Uri("/api/v1/live/connectors", UriKind.Relative), ApiJson.Options, ct);
        var bySite = await paul.GetFromJsonAsync<List<Gatehouse.Application.LiveBuilding.Dtos.ConnectorHealthDto>>(new Uri($"/api/v1/live/connectors?siteId={hqSite}", UriKind.Relative), ApiJson.Options, ct);

        Assert.DoesNotContain(all!, c => nordlysInstances.Contains(c.PacsInstanceId));
        Assert.Empty(bySite!);
    }

    [Fact]
    public async Task Live_floor_of_other_tenant_is_404()
    {
        var ct = TestContext.Current.CancellationToken;
        Guid floorId;
        await using (var scope = await _factory.CreateTenantScopeAsync("nordlys"))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            floorId = (await db.Areas.AsNoTracking().SingleAsync(a => a.Code == "CPH-VAULT", ct)).FloorId.Value;
        }

        using var paul = _factory.CreateClientFor(DemoUser.Paul);
        using var response = await paul.GetAsync(new Uri($"/api/v1/live/floors/{floorId}", UriKind.Relative), ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Alarms_of_other_tenant_are_404()
    {
        var ct = TestContext.Current.CancellationToken;
        Domain.LiveBuilding.AlarmId alarmId;
        await using (var scope = await _factory.CreateTenantScopeAsync("nordlys"))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var doorId = await db.Doors.AsNoTracking().Select(d => d.Id).FirstAsync(ct);
            var alarm = Domain.LiveBuilding.Alarm.Raise(doorId, Domain.LiveBuilding.AlarmKind.Forced, Guid.CreateVersion7(), DateTimeOffset.UtcNow);
            db.Alarms.Add(alarm);
            await db.SaveChangesAsync(ct);
            alarmId = alarm.Id;
        }

        try
        {
            using var paul = _factory.CreateClientFor(DemoUser.Paul);
            using var ack = await paul.PostAsJsonAsync(new Uri($"/api/v1/live/alarms/{alarmId.Value}/acknowledge", UriKind.Relative), new { comment = "x" }, ct);
            var list = await paul.GetFromJsonAsync<List<Gatehouse.Application.LiveBuilding.Dtos.AlarmDto>>(new Uri("/api/v1/live/alarms", UriKind.Relative), ApiJson.Options, ct);

            Assert.Equal(HttpStatusCode.NotFound, ack.StatusCode);
            Assert.NotNull(list);
            Assert.DoesNotContain(list, a => a.AlarmId == alarmId.Value);
        }
        finally
        {
            await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            await db.Alarms.Where(a => a.Id == alarmId).ExecuteDeleteAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Nordlys_fulfillment_id_is_404_for_bricks_staff()
    {
        using var jonas = _factory.CreateClientFor(DemoUser.Jonas);
        var items = await Fulfillment.FulfillmentQueueEndpointTests.ListAsync(jonas, "done");
        await AssertNotFoundForClientAsync(DemoUser.Paul, $"/api/v1/fulfillments/{items.First().FulfillmentId}");
    }

    private async Task AssertNotFoundForClientAsync(DemoUser user, string url)
    {
        using var client = _factory.CreateClientFor(user);
        using var response = await client.GetAsync(new Uri(url, UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Cards_of_other_tenant_person_are_404()
    {
        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var anna = await db.Persons.AsNoTracking().SingleAsync(p => p.Email == DemoUser.Anna.Email, TestContext.Current.CancellationToken);
        await AssertNotFoundForBricksAsync($"/api/v1/people/{anna.Id.Value}/cards");
    }

    [Fact]
    public async Task Person_of_other_tenant_is_404()
    {
        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var anna = await db.Persons.AsNoTracking().SingleAsync(p => p.Email == DemoUser.Anna.Email, TestContext.Current.CancellationToken);
        await AssertNotFoundForBricksAsync($"/api/v1/people/{anna.Id.Value}");
    }

    [Fact]
    public async Task Photo_of_other_tenant_person_is_404()
    {
        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var anna = await db.Persons.AsNoTracking().SingleAsync(p => p.Email == DemoUser.Anna.Email, TestContext.Current.CancellationToken);
        await AssertNotFoundForBricksAsync($"/api/v1/people/{anna.Id.Value}/photo");
    }

    [Fact]
    public async Task Floor_events_of_other_tenant_are_404()
    {
        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var floor = await db.Floors.AsNoTracking().FirstAsync(TestContext.Current.CancellationToken);
        using var paul = _factory.CreateClientFor(DemoUser.Paul);
        using var response = await paul.GetAsync(new Uri($"/api/v1/live/floors/{floor.Id.Value}/events", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Dashboard_counts_only_own_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        using var paul = _factory.CreateClientFor(DemoUser.Paul);
        var dashboard = await paul.GetFromJsonAsync<Gatehouse.Application.Dashboard.DashboardDto>(new Uri("/api/v1/dashboard", UriKind.Relative), ApiJson.Options, ct);
        await using var scope = await _factory.CreateTenantScopeAsync("bricks");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        Assert.Equal(await db.Entitlements.CountAsync(e => e.Status == Gatehouse.Domain.Access.EntitlementStatus.PendingApproval, ct), dashboard!.PendingApprovals);
    }

    [Fact]
    public async Task Theme_is_per_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        using var lena = _factory.CreateClientFor(DemoUser.Lena);
        using var anna = _factory.CreateClientFor(DemoUser.Anna);

        var bricks = await lena.GetFromJsonAsync<Gatehouse.Application.Theming.ThemeDto>(new Uri("/api/v1/theme", UriKind.Relative), ApiJson.Options, ct);
        var nordlys = await anna.GetFromJsonAsync<Gatehouse.Application.Theming.ThemeDto>(new Uri("/api/v1/theme", UriKind.Relative), ApiJson.Options, ct);

        Assert.Equal("Zone", bricks!.Terms["Area"]);
        Assert.False(nordlys!.Terms.ContainsKey("Area"));
        Assert.NotEqual(bricks.Primary, nordlys.Primary);
    }

    private async Task<Guid> NordlysAreaIdAsync(string code)
    {
        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        return (await db.Areas.SingleAsync(a => a.Code == code, TestContext.Current.CancellationToken)).Id.Value;
    }

    [Fact]
    public async Task Identity_provider_is_per_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        var summary = new Gatehouse.Application.Identity.IdentityProviders.TenantIdentityProvider(
            Gatehouse.Domain.Tenancy.IdpKind.Oidc, "nordlys-oidc", "Nordlys Entra", "https://login.nordlys.example/.well-known/openid-configuration",
            "https://login.nordlys.example", "gatehouse", null, null, null, DateTimeOffset.UtcNow, null, null);
        await using (var scope = await _factory.CreateTenantScopeAsync("nordlys"))
        {
            var store = scope.ServiceProvider.GetRequiredService<Gatehouse.Application.Identity.IdentityProviders.IdentityProviderStore>();
            await store.SetAsync(summary, DateTimeOffset.UtcNow, ct);
            await scope.ServiceProvider.GetRequiredService<TenantDbContext>().SaveChangesAsync(ct);
        }

        _factory.Services.GetRequiredService<FakeIdentityProvisioner>().Providers[new Gatehouse.Domain.Common.TenantId("nordlys")] =
            new Gatehouse.Application.Tenancy.IdentityProvisioning.ProvisionedProvider("nordlys-oidc", Gatehouse.Domain.Tenancy.IdpKind.Oidc, "http://localhost:8080/realms/gatehouse/broker/nordlys-oidc/endpoint", null);

        using var mia = _factory.CreateClientFor(DemoUser.Mia);
        using var bricks = await mia.GetAsync(new Uri("/api/v1/admin/identity-provider", UriKind.Relative), ct);
        Assert.Equal(HttpStatusCode.NoContent, bricks.StatusCode);

        using var freja = _factory.CreateClientFor(DemoUser.Freja);
        using var nordlys = await freja.GetAsync(new Uri("/api/v1/admin/identity-provider", UriKind.Relative), ct);
        Assert.Equal(HttpStatusCode.OK, nordlys.StatusCode);
        Assert.Contains("nordlys-oidc", await nordlys.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_site_routes_are_tenant_scoped()
    {
        var ct = TestContext.Current.CancellationToken;
        Guid siteId, floorId;
        await using (var scope = await _factory.CreateTenantScopeAsync("nordlys"))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var floor = await db.Floors.AsNoTracking().FirstAsync(ct);
            (siteId, floorId) = (floor.SiteId.Value, floor.Id.Value);
        }

        using var mia = _factory.CreateClientFor(DemoUser.Mia);
        using var updateSite = await mia.PutAsJsonAsync(new Uri($"/api/v1/admin/sites/{siteId}", UriKind.Relative), new SaveSiteRequest("Hijack", "HIJACK", "Europe/Berlin"), ct);
        using var deleteSite = await mia.DeleteAsync(new Uri($"/api/v1/admin/sites/{siteId}", UriKind.Relative), ct);
        using var addFloor = await mia.PostAsJsonAsync(new Uri($"/api/v1/admin/sites/{siteId}/floors", UriKind.Relative), new SaveFloorRequest("X", 99, 1000, 1000), ct);
        using var deleteFloor = await mia.DeleteAsync(new Uri($"/api/v1/admin/floors/{floorId}", UriKind.Relative), ct);

        Assert.All([updateSite, deleteSite, addFloor, deleteFloor], r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));
    }

    [Fact]
    public async Task Floor_plan_of_other_tenant_is_404()
    {
        Guid floorId;
        await using (var scope = await _factory.CreateTenantScopeAsync("nordlys"))
        {
            floorId = (await scope.ServiceProvider.GetRequiredService<TenantDbContext>().Floors.AsNoTracking().Select(f => f.Id).FirstAsync(TestContext.Current.CancellationToken)).Value;
        }

        using var mia = _factory.CreateClientFor(DemoUser.Mia);
        using var response = await mia.GetAsync(new Uri($"/api/v1/admin/floors/{floorId}/plan", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Floor_background_of_other_tenant_is_404()
    {
        var ct = TestContext.Current.CancellationToken;
        Guid floorId;
        await using (var scope = await _factory.CreateTenantScopeAsync("nordlys"))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var floor = await db.Floors.FirstAsync(ct);
            floor.SetBackground($"tenant/nordlys/floors/{floor.Id.Value}/isolation.png", "image/png", 10, 10, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(ct);
            floorId = floor.Id.Value;
        }

        await AssertNotFoundForBricksAsync($"/api/v1/floors/{floorId}/background");
        using var mia = _factory.CreateClientFor(DemoUser.Mia);
        using var delete = await mia.DeleteAsync(new Uri($"/api/v1/admin/floors/{floorId}/background", UriKind.Relative), ct);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    [Fact]
    public async Task Role_assignments_are_tenant_scoped()
    {
        var ct = TestContext.Current.CancellationToken;
        Guid nordlysAssignment;
        await using (var scope = await _factory.CreateTenantScopeAsync("nordlys"))
        {
            nordlysAssignment = (await scope.ServiceProvider.GetRequiredService<TenantDbContext>().RoleAssignments.AsNoTracking().Select(r => r.Id).FirstAsync(ct)).Value;
        }

        using var mia = _factory.CreateClientFor(DemoUser.Mia);
        var list = (await mia.GetFromJsonAsync<List<Gatehouse.Application.People.Dtos.RoleAssignmentDto>>(new Uri("/api/v1/admin/role-assignments", UriKind.Relative), ApiJson.Options, ct))!;
        Assert.DoesNotContain(list, r => r.Id == nordlysAssignment);
        Assert.Contains(list, r => r.Person.DisplayName.Contains("Mia", StringComparison.Ordinal));
        using var delete = await mia.DeleteAsync(new Uri($"/api/v1/admin/role-assignments/{nordlysAssignment}", UriKind.Relative), ct);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    [Fact]
    public async Task Pacs_instance_of_other_tenant_is_404()
    {
        var pacsId = await NordlysPacsIdAsync();
        using var mia = _factory.CreateClientFor(DemoUser.Mia);
        using var get = await mia.GetAsync(new Uri($"/api/v1/admin/pacs-instances/{pacsId}", UriKind.Relative), TestContext.Current.CancellationToken);
        using var delete = await mia.DeleteAsync(new Uri($"/api/v1/admin/pacs-instances/{pacsId}", UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        var list = (await mia.GetFromJsonAsync<List<Gatehouse.Application.Fulfillment.PacsAdmin.PacsInstanceAdminDto>>(new Uri("/api/v1/admin/pacs-instances", UriKind.Relative), ApiJson.Options, TestContext.Current.CancellationToken))!;
        Assert.DoesNotContain(list, p => p.Id == pacsId);
    }

    [Fact]
    public async Task Connector_registration_of_other_tenant_is_404()
    {
        var pacsId = await NordlysPacsIdAsync();
        using var mia = _factory.CreateClientFor(DemoUser.Mia);
        using var register = await mia.PostAsync(new Uri($"/api/v1/admin/pacs-instances/{pacsId}/connector", UriKind.Relative), null, TestContext.Current.CancellationToken);
        using var revoke = await mia.PostAsync(new Uri($"/api/v1/admin/pacs-instances/{pacsId}/connector/revoke", UriKind.Relative), null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, register.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, revoke.StatusCode);
    }

    private async Task<Guid> NordlysPacsIdAsync()
    {
        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        return (await scope.ServiceProvider.GetRequiredService<TenantDbContext>().PacsInstances.AsNoTracking().Select(p => p.Id).FirstAsync(TestContext.Current.CancellationToken)).Value;
    }

    [Fact]
    public async Task Onboarding_status_is_per_tenant()
    {
        var ct = TestContext.Current.CancellationToken;
        using var freja = _factory.CreateClientFor(DemoUser.Freja);
        using var skip = await freja.PostAsync(new Uri("/api/v1/admin/onboarding/steps/Connector/skip", UriKind.Relative), null, ct);
        Assert.Equal(HttpStatusCode.OK, skip.StatusCode);

        using var mia = _factory.CreateClientFor(DemoUser.Mia);
        var bricks = (await mia.GetFromJsonAsync<Gatehouse.Application.Tenancy.Onboarding.OnboardingStatusDto>(new Uri("/api/v1/admin/onboarding", UriKind.Relative), ApiJson.Options, ct))!;

        Assert.NotEqual(Gatehouse.Application.Tenancy.Onboarding.OnboardingStepState.Skipped,
            bricks.Steps.Single(s => s.Step == Gatehouse.Application.Tenancy.Onboarding.OnboardingStep.Connector).State);
    }

    private async Task AssertNotFoundForBricksAsync(string url)
    {
        using var client = _factory.CreateClientFor(DemoUser.Lena);
        using var response = await client.GetAsync(new Uri(url, UriKind.Relative), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Connector_heartbeat_only_touches_own_instance()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var gateway = new GatewayFactory(postgres);
        await gateway.InitializeAsync();
        try
        {
            await Connectors.ConnectorTestEnrollment.ResetAsync(gateway);
            using var cert = await Connectors.ConnectorTestEnrollment.EnrollAsync(gateway, "nordlys", "HQ-Copenhagen");
            using var client = gateway.CreateClient();
            var response = await Connectors.ConnectorTestEnrollment.PostAsync(client, "/connector/v1/heartbeat", Connectors.ConnectorTestEnrollment.NewHeartbeat(), cert);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            foreach (var tenant in new[] { "nordlys", "bricks" })
            {
                await using var scope = await Connectors.ConnectorTestEnrollment.ScopeAsync(gateway, tenant);
                var instances = await scope.ServiceProvider.GetRequiredService<TenantDbContext>().PacsInstances.AsNoTracking().ToListAsync(ct);
                var touched = instances.Where(p => p.LastHeartbeatAt is not null).Select(p => p.Name).ToList();
                Assert.Equal(tenant == "nordlys" ? new List<string> { "HQ-Copenhagen" } : new List<string>(), touched);
            }
        }
        finally
        {
            await Connectors.ConnectorTestEnrollment.ResetAsync(gateway);
        }
    }

    [Fact]
    public async Task Poll_never_returns_other_instance_commands()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var gateway = new GatewayFactory(postgres);
        await gateway.InitializeAsync();
        try
        {
            await Connectors.ConnectorTestEnrollment.ResetAsync(gateway);
            await using (var scope = await Connectors.ConnectorTestEnrollment.ScopeAsync(gateway, "bricks"))
            {
                var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
                var pacs = await db.PacsInstances.AsNoTracking().FirstAsync(ct);
                db.ConnectorCommands.Add(Gatehouse.Domain.Connectors.ConnectorCommand.Create(pacs.Id, "UpsertCardholder", "person:x", 1, Gatehouse.Domain.Fulfillment.CommandPriority.Normal, "x", "{}", null, DateTimeOffset.UtcNow));
                await db.SaveChangesAsync(ct);
            }

            using var cert = await Connectors.ConnectorTestEnrollment.EnrollAsync(gateway, "nordlys", "HQ-Copenhagen");
            using var client = gateway.CreateClient();
            var response = await Connectors.ConnectorTestEnrollment.PostAsync(client, "/connector/v1/poll", new Gatehouse.Connector.Contracts.Protocol.PollRequest(50, 0), cert);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<Gatehouse.Connector.Contracts.Protocol.PollResponse>(Gatehouse.Connector.Contracts.ConnectorJson.Options, ct);
            Assert.Empty(body!.Commands);
        }
        finally
        {
            await Connectors.ConnectorTestEnrollment.ResetAsync(gateway);
            await using var scope = await Connectors.ConnectorTestEnrollment.ScopeAsync(gateway, "bricks");
            await scope.ServiceProvider.GetRequiredService<TenantDbContext>().ConnectorCommands.ExecuteDeleteAsync(ct);
        }
    }
}
