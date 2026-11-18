using System.Net;
using System.Net.Http.Json;
using Gatehouse.Application.Access.Dtos;
using Gatehouse.Application.Fulfillment.PacsAdmin;
using Gatehouse.Application.Identity;
using Gatehouse.Application.Identity.IdentityProviders;
using Gatehouse.Application.People.Dtos;
using Gatehouse.Application.Platform.Provisioning;
using Gatehouse.Application.Sites.Dtos;
using Gatehouse.Application.Tenancy.Onboarding;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Tenancy;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Gatehouse.IntegrationTests.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Tenancy;

/// <summary><c>domain-model.md</c> §2.10 onboarding steps on a freshly provisioned tenant.</summary>
public sealed class OnboardingStatusTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly Uri Status = new("/api/v1/admin/onboarding", UriKind.Relative);
    private readonly GatehouseApiFactory _api = new(postgres);

    public ValueTask InitializeAsync() => _api.InitializeAsync();

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Fresh_tenant_has_all_steps_not_started()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await FreshTenantAdminAsync(ct);

        var status = await StatusAsync(admin, ct);

        Assert.Equal(Enum.GetValues<OnboardingStep>(), status.Steps.Select(s => s.Step));
        Assert.All(status.Steps, s => Assert.Equal(OnboardingStepState.NotStarted, s.State));
        Assert.False(status.CanComplete);
        Assert.Null(status.CompletedAt);
        Assert.All(status.Steps, s => Assert.StartsWith("/admin/", s.Href, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Steps_complete_as_data_appears()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = await ProvisionAsync(ct);
        using var admin = AdminClient(tenant);
        var (_, areaId) = await ModelSiteAsync(admin, tenant, ct);
        using (var employee = await admin.PostAsJsonAsync(new Uri("/api/v1/admin/people/employees", UriKind.Relative), new CreateEmployeeRequest($"staff@{tenant.EmailDomain}", "Sam", "Staff", null, null), ApiJson.Options, ct))
        {
            Assert.Equal(HttpStatusCode.Created, employee.StatusCode);
            var dto = (await employee.Content.ReadFromJsonAsync<PersonSummaryDto>(ApiJson.Options, ct))!;
            using var role = await admin.PostAsJsonAsync(new Uri("/api/v1/admin/role-assignments", UriKind.Relative), new AssignRoleRequest(dto.PersonId, AssignedRole.SecurityStaff, null), ApiJson.Options, ct);
            Assert.Equal(HttpStatusCode.Created, role.StatusCode);
        }

        var pacs = await CreatePacsAsync(admin, tenant, ct);
        using (var map = await admin.PutAsJsonAsync(new Uri($"/api/v1/admin/pacs-instances/{pacs.Id}/mappings", UriKind.Relative), new[] { new AccessLevelMappingRequest(areaId, "AL-1", null) }, ApiJson.Options, ct))
        {
            Assert.Equal(HttpStatusCode.OK, map.StatusCode);
        }

        await SaveIdpSummaryAsync(tenant, ct);

        var status = await StatusAsync(admin, ct);

        Assert.Equal(OnboardingStepState.Complete, State(status, OnboardingStep.IdentityProvider));
        Assert.Equal(OnboardingStepState.Complete, State(status, OnboardingStep.Sites));
        Assert.Equal(OnboardingStepState.Complete, State(status, OnboardingStep.People));
        Assert.Equal(OnboardingStepState.Complete, State(status, OnboardingStep.Pacs));
        Assert.Equal(OnboardingStepState.Complete, State(status, OnboardingStep.Staff));
        Assert.Equal(OnboardingStepState.NotStarted, State(status, OnboardingStep.Connector));
        Assert.False(status.CanComplete);

        using var skip = await admin.PostAsync(new Uri("/api/v1/admin/onboarding/steps/Connector/skip", UriKind.Relative), null, ct);
        Assert.Equal(HttpStatusCode.OK, skip.StatusCode);
        using var complete = await admin.PostAsync(new Uri("/api/v1/admin/onboarding/complete", UriKind.Relative), null, ct);
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);
        Assert.NotNull((await complete.Content.ReadFromJsonAsync<OnboardingStatusDto>(ApiJson.Options, ct))!.CompletedAt);
    }

    [Fact]
    public async Task Unmapped_area_keeps_pacs_in_progress()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = await ProvisionAsync(ct);
        using var admin = AdminClient(tenant);
        await ModelSiteAsync(admin, tenant, ct);
        await CreatePacsAsync(admin, tenant, ct);

        var status = await StatusAsync(admin, ct);

        Assert.Equal(OnboardingStepState.InProgress, State(status, OnboardingStep.Pacs));
    }

    [Fact]
    public async Task Pacs_on_empty_site_does_not_cover_populated_site()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = await ProvisionAsync(ct);
        using var admin = AdminClient(tenant);
        await ModelSiteAsync(admin, tenant, ct);
        using var branchResponse = await admin.PostAsJsonAsync(new Uri("/api/v1/admin/sites", UriKind.Relative), new SaveSiteRequest("Branch", "BR", "Europe/Copenhagen"), ApiJson.Options, ct);
        var branch = (await branchResponse.Content.ReadFromJsonAsync<SiteDto>(ApiJson.Options, ct))!;
        using var pacs = await admin.PostAsJsonAsync(new Uri("/api/v1/admin/pacs-instances", UriKind.Relative), new SavePacsInstanceRequest("Branch PACS", PacsVendor.LegacyPacs, [branch.SiteId]), ApiJson.Options, ct);
        Assert.Equal(HttpStatusCode.Created, pacs.StatusCode);

        var step = (await StatusAsync(admin, ct)).Steps.Single(s => s.Step == OnboardingStep.Pacs);

        Assert.Equal(OnboardingStepState.InProgress, step.State);
        Assert.Contains("no access control system", step.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Offline_connector_keeps_connector_in_progress()
    {
        var ct = TestContext.Current.CancellationToken;
        var tenant = await ProvisionAsync(ct);
        using var admin = AdminClient(tenant);
        await ModelSiteAsync(admin, tenant, ct);
        var pacs = await CreatePacsAsync(admin, tenant, ct);
        using var register = await admin.PostAsync(new Uri($"/api/v1/admin/pacs-instances/{pacs.Id}/connector", UriKind.Relative), null, ct);
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        var status = await StatusAsync(admin, ct);

        Assert.Equal(OnboardingStepState.InProgress, State(status, OnboardingStep.Connector));
    }

    [Fact]
    public async Task Only_connector_and_staff_are_skippable()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await FreshTenantAdminAsync(ct);

        using var sites = await admin.PostAsync(new Uri("/api/v1/admin/onboarding/steps/Sites/skip", UriKind.Relative), null, ct);
        using var staff = await admin.PostAsync(new Uri("/api/v1/admin/onboarding/steps/Staff/skip", UriKind.Relative), null, ct);

        Assert.Equal(HttpStatusCode.Conflict, sites.StatusCode);
        Assert.Contains("onboarding.step_not_skippable", await sites.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, staff.StatusCode);
        var status = (await staff.Content.ReadFromJsonAsync<OnboardingStatusDto>(ApiJson.Options, ct))!;
        Assert.Equal(OnboardingStepState.Skipped, State(status, OnboardingStep.Staff));
        Assert.Equal([OnboardingStep.Connector, OnboardingStep.Staff], status.Steps.Where(s => s.Skippable).Select(s => s.Step));
    }

    [Fact]
    public async Task Complete_requires_required_steps()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await FreshTenantAdminAsync(ct);
        using var skipConnector = await admin.PostAsync(new Uri("/api/v1/admin/onboarding/steps/Connector/skip", UriKind.Relative), null, ct);
        using var skipStaff = await admin.PostAsync(new Uri("/api/v1/admin/onboarding/steps/Staff/skip", UriKind.Relative), null, ct);

        using var response = await admin.PostAsync(new Uri("/api/v1/admin/onboarding/complete", UriKind.Relative), null, ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("onboarding.incomplete", await response.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Me_reports_onboarding_pending_for_admin_only()
    {
        var ct = TestContext.Current.CancellationToken;
        using var admin = await FreshTenantAdminAsync(ct);
        using var anna = _api.CreateClientFor(DemoUser.Anna);

        var adminMe = (await admin.GetFromJsonAsync<MeDto>(new Uri("/api/v1/me", UriKind.Relative), ApiJson.Options, ct))!;
        var annaMe = (await anna.GetFromJsonAsync<MeDto>(new Uri("/api/v1/me", UriKind.Relative), ApiJson.Options, ct))!;

        Assert.True(adminMe.OnboardingPending);
        Assert.False(annaMe.OnboardingPending);
    }

    private static OnboardingStepState State(OnboardingStatusDto status, OnboardingStep step) => status.Steps.Single(s => s.Step == step).State;

    private static async Task<OnboardingStatusDto> StatusAsync(HttpClient client, CancellationToken ct) =>
        (await client.GetFromJsonAsync<OnboardingStatusDto>(Status, ApiJson.Options, ct))!;

    private async Task<HttpClient> FreshTenantAdminAsync(CancellationToken ct) => AdminClient(await ProvisionAsync(ct));

    private HttpClient AdminClient(Gatehouse.Application.Platform.Dtos.CreateTenantRequest tenant) =>
        _api.CreateClientFor($"kc-{tenant.TenantId}", tenant.AdminEmail, tenant.TenantId);

    private async Task<Gatehouse.Application.Platform.Dtos.CreateTenantRequest> ProvisionAsync(CancellationToken ct)
    {
        using var platform = await PlatformTestData.OperatorClientAsync(_api, ct);
        var request = PlatformTestData.NewTenant();
        using var created = await PlatformTestData.CreateAsync(platform, request, ct);
        Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);
        await _api.Services.GetRequiredService<ITenantProvisioningRunner>().RunOnceAsync(ct);
        return request;
    }

    private static async Task<(Guid SiteId, Guid AreaId)> ModelSiteAsync(HttpClient admin, Gatehouse.Application.Platform.Dtos.CreateTenantRequest tenant, CancellationToken ct)
    {
        using var siteResponse = await admin.PostAsJsonAsync(new Uri("/api/v1/admin/sites", UriKind.Relative), new SaveSiteRequest("Head office", "HQ", "Europe/Copenhagen"), ApiJson.Options, ct);
        var site = (await siteResponse.Content.ReadFromJsonAsync<SiteDto>(ApiJson.Options, ct))!;
        using var floorResponse = await admin.PostAsJsonAsync(new Uri($"/api/v1/admin/sites/{site.SiteId}/floors", UriKind.Relative), new SaveFloorRequest("Ground", 0, 1000, 800), ApiJson.Options, ct);
        var floor = (await floorResponse.Content.ReadFromJsonAsync<AdminFloorDto>(ApiJson.Options, ct))!;
        var me = (await admin.GetFromJsonAsync<MeDto>(new Uri("/api/v1/me", UriKind.Relative), ApiJson.Options, ct))!;
        var levels = (await admin.GetFromJsonAsync<List<SecurityLevelAdminDto>>(new Uri("/api/v1/admin/security-levels", UriKind.Relative), ApiJson.Options, ct))!;
        using var areaResponse = await admin.PostAsJsonAsync(new Uri($"/api/v1/admin/floors/{floor.FloorId}/areas", UriKind.Relative),
            new SaveAreaRequest("Lobby", "HQ-LOBBY", [new PointDto(100, 100), new PointDto(400, 100), new PointDto(400, 400), new PointDto(100, 400)], levels[0].Id, [me.Person!.PersonId]), ApiJson.Options, ct);
        Assert.True(areaResponse.StatusCode == HttpStatusCode.Created, await areaResponse.Content.ReadAsStringAsync(ct));
        var area = (await areaResponse.Content.ReadFromJsonAsync<PlanAreaDto>(ApiJson.Options, ct))!;
        using var door = await admin.PostAsJsonAsync(new Uri($"/api/v1/admin/areas/{area.Id}/doors", UriKind.Relative), new SaveDoorRequest("Main door", new PointDto(250, 100), "D-1"), ApiJson.Options, ct);
        Assert.Equal(HttpStatusCode.Created, door.StatusCode);
        return (site.SiteId, area.Id);
    }

    private static async Task<PacsInstanceAdminDto> CreatePacsAsync(HttpClient admin, Gatehouse.Application.Platform.Dtos.CreateTenantRequest tenant, CancellationToken ct)
    {
        var sites = (await admin.GetFromJsonAsync<List<SiteDto>>(new Uri("/api/v1/sites", UriKind.Relative), ApiJson.Options, ct))!;
        using var response = await admin.PostAsJsonAsync(new Uri("/api/v1/admin/pacs-instances", UriKind.Relative), new SavePacsInstanceRequest("Main PACS", PacsVendor.LegacyPacs, [sites[0].SiteId]), ApiJson.Options, ct);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<PacsInstanceAdminDto>(ApiJson.Options, ct))!;
    }

    private async Task SaveIdpSummaryAsync(Gatehouse.Application.Platform.Dtos.CreateTenantRequest tenant, CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync(tenant.TenantId);
        await scope.ServiceProvider.GetRequiredService<IdentityProviderStore>().SetAsync(
            new TenantIdentityProvider(IdpKind.Oidc, $"{tenant.TenantId}-oidc", "Customer SSO", "https://sso.example/.well-known/openid-configuration", "https://sso.example", "gatehouse",
                null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, true), DateTimeOffset.UtcNow, ct);
        await scope.ServiceProvider.GetRequiredService<TenantDbContext>().SaveChangesAsync(ct);
    }
}
