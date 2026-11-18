using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using Gatehouse.Application.Fulfillment.PacsAdmin;
using Gatehouse.Application.Fulfillment.PacsAdmin.RegisterConnector;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Protocol;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Connectors;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Fulfillment;

/// <summary>authorization.md §11 PACS rows, domain-model.md §1 ConnectorRegistration (M10 note).</summary>
public sealed class PacsAdminApiTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly Uri Instances = new("/api/v1/admin/pacs-instances", UriKind.Relative);
    private readonly GatehouseApiFactory _api = new(postgres);

    public ValueTask InitializeAsync() => _api.InitializeAsync();

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Create_instance_and_map_areas()
    {
        var ct = TestContext.Current.CancellationToken;
        using var freja = _api.CreateClientFor(DemoUser.Freja);
        var (siteId, areaIds) = await SiteWithAreasAsync(ct);

        var instance = await CreateAsync(freja, siteId, ct);
        using var map = await freja.PutAsJsonAsync(new Uri($"/api/v1/admin/pacs-instances/{instance.Id}/mappings", UriKind.Relative),
            new[] { new AccessLevelMappingRequest(areaIds[0], "AL-100", "Lobby level"), new AccessLevelMappingRequest(areaIds[1], "AL-200", null) }, ApiJson.Options, ct);

        Assert.Equal(HttpStatusCode.OK, map.StatusCode);
        var dto = (await map.Content.ReadFromJsonAsync<PacsInstanceAdminDto>(ApiJson.Options, ct))!;
        Assert.Equal(2, dto.Mappings.Count);
        Assert.Contains(dto.Mappings, m => m.VendorAccessLevelRef == "AL-200" && m.DisplayName == "AL-200");
        Assert.Equal(areaIds.Count - 2, dto.UnmappedAreas);
        Assert.Equal(FulfillmentMode.Manual, dto.FulfillmentMode);
        Assert.Null(dto.Connector);

        using var duplicate = await freja.PostAsJsonAsync(Instances, new SavePacsInstanceRequest(instance.Name, PacsVendor.LegacyPacs, [siteId]), ApiJson.Options, ct);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Contains("pacs.name_taken", await duplicate.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mapping_area_outside_sites_is_400()
    {
        var ct = TestContext.Current.CancellationToken;
        using var freja = _api.CreateClientFor(DemoUser.Freja);
        var (siteId, _) = await SiteWithAreasAsync(ct);
        var otherArea = await AreaOutsideAsync(siteId, ct);
        var instance = await CreateAsync(freja, siteId, ct);

        using var response = await freja.PutAsJsonAsync(new Uri($"/api/v1/admin/pacs-instances/{instance.Id}/mappings", UriKind.Relative),
            new[] { new AccessLevelMappingRequest(otherArea, "AL-9", null) }, ApiJson.Options, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("mapping.area_not_on_instance_site", await response.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Register_connector_returns_token_once_and_enrolls()
    {
        var ct = TestContext.Current.CancellationToken;
        using var freja = _api.CreateClientFor(DemoUser.Freja);
        var (siteId, _) = await SiteWithAreasAsync(ct);
        var instance = await CreateAsync(freja, siteId, ct);

        using var response = await freja.PostAsync(new Uri($"/api/v1/admin/pacs-instances/{instance.Id}/connector", UriKind.Relative), null, ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var enrollment = (await response.Content.ReadFromJsonAsync<ConnectorEnrollmentDto>(ApiJson.Options, ct))!;
        Assert.StartsWith("ghe_", enrollment.EnrollmentToken, StringComparison.Ordinal);
        Assert.Contains("BEGIN CERTIFICATE", enrollment.CaCertificatePem, StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(enrollment.GatewayUrl));
        Assert.Null(enrollment.AgentImage);

        var get = (await freja.GetFromJsonAsync<PacsInstanceAdminDto>(new Uri($"/api/v1/admin/pacs-instances/{instance.Id}", UriKind.Relative), ApiJson.Options, ct))!;
        Assert.Equal(FulfillmentMode.Connector, get.FulfillmentMode);
        Assert.Equal(Gatehouse.Domain.Connectors.ConnectorStatus.PendingEnrollment, get.Connector!.Status);
        Assert.DoesNotContain(enrollment.EnrollmentToken, await (await freja.GetAsync(new Uri($"/api/v1/admin/pacs-instances/{instance.Id}", UriKind.Relative), ct)).Content.ReadAsStringAsync(ct), StringComparison.Ordinal);

        await using var gateway = new GatewayFactory(postgres, _api.CatalogConnectionString);
        await gateway.InitializeAsync();
        using var enrolled = await EnrollAsync(gateway, enrollment.EnrollmentToken, ct);
        Assert.Equal(HttpStatusCode.OK, enrolled.StatusCode);
        using var replay = await EnrollAsync(gateway, enrollment.EnrollmentToken, ct);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
    }

    [Fact]
    public async Task Enrollment_includes_configured_agent_image()
    {
        var ct = TestContext.Current.CancellationToken;
        _api.Services.GetRequiredService<IConfiguration>()[RegisterConnectorHandler.AgentImageSetting] = "ghcr.io/example/gatehouse-agent:1.2.3";
        using var freja = _api.CreateClientFor(DemoUser.Freja);
        var (siteId, _) = await SiteWithAreasAsync(ct);
        var instance = await CreateAsync(freja, siteId, ct);

        using var response = await freja.PostAsync(new Uri($"/api/v1/admin/pacs-instances/{instance.Id}/connector", UriKind.Relative), null, ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var enrollment = (await response.Content.ReadFromJsonAsync<ConnectorEnrollmentDto>(ApiJson.Options, ct))!;
        Assert.Equal("ghcr.io/example/gatehouse-agent:1.2.3", enrollment.AgentImage);
    }

    [Fact]
    public async Task Second_registration_is_409()
    {
        var ct = TestContext.Current.CancellationToken;
        using var freja = _api.CreateClientFor(DemoUser.Freja);
        var (siteId, _) = await SiteWithAreasAsync(ct);
        var instance = await CreateAsync(freja, siteId, ct);
        using var first = await freja.PostAsync(new Uri($"/api/v1/admin/pacs-instances/{instance.Id}/connector", UriKind.Relative), null, ct);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        using var second = await freja.PostAsync(new Uri($"/api/v1/admin/pacs-instances/{instance.Id}/connector", UriKind.Relative), null, ct);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("connector.already_exists", await second.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Revoke_returns_instance_to_manual_and_gateway_rejects_cert()
    {
        var ct = TestContext.Current.CancellationToken;
        using var freja = _api.CreateClientFor(DemoUser.Freja);
        var (siteId, _) = await SiteWithAreasAsync(ct);
        var instance = await CreateAsync(freja, siteId, ct);
        using var register = await freja.PostAsync(new Uri($"/api/v1/admin/pacs-instances/{instance.Id}/connector", UriKind.Relative), null, ct);
        var enrollment = (await register.Content.ReadFromJsonAsync<ConnectorEnrollmentDto>(ApiJson.Options, ct))!;
        await using var gateway = new GatewayFactory(postgres, _api.CatalogConnectionString);
        await gateway.InitializeAsync();
        var (csrPem, key) = ConnectorTestEnrollment.CreateCsr();
        using var enrolled = await EnrollAsync(gateway, enrollment.EnrollmentToken, ct, csrPem);
        var body = (await enrolled.Content.ReadFromJsonAsync<EnrollResponse>(ConnectorJson.Options, ct))!;
        using var certificate = X509Certificate2.CreateFromPem(body.CertificatePem);
        using var cert = certificate.CopyWithPrivateKey(key);

        using var revoke = await freja.PostAsync(new Uri($"/api/v1/admin/pacs-instances/{instance.Id}/connector/revoke", UriKind.Relative), null, ct);

        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        var get = (await freja.GetFromJsonAsync<PacsInstanceAdminDto>(new Uri($"/api/v1/admin/pacs-instances/{instance.Id}", UriKind.Relative), ApiJson.Options, ct))!;
        Assert.Equal(FulfillmentMode.Manual, get.FulfillmentMode);
        Assert.Null(get.Connector);
        using var client = gateway.CreateClient();
        using var heartbeat = await ConnectorTestEnrollment.PostAsync(client, "/connector/v1/heartbeat", ConnectorTestEnrollment.NewHeartbeat(), cert);
        Assert.Equal(HttpStatusCode.Forbidden, heartbeat.StatusCode);
        Assert.Contains("connector.revoked", await heartbeat.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
        using var again = await freja.PostAsync(new Uri($"/api/v1/admin/pacs-instances/{instance.Id}/connector/revoke", UriKind.Relative), null, ct);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    [Fact]
    public async Task Mapping_edit_plans_revoke_old_and_grant_new()
    {
        await MappingMigrationTests.ConnectorModeAsync(_api);
        var (entitlementId, oldLevel) = await MappingMigrationTests.ConfirmedConnectorGrantAsync(_api);

        await MappingMigrationTests.EditMappingAsync(_api, MappingMigrationTests.NewLevel);

        var planned = await MappingMigrationTests.FulfillmentsAsync(_api, entitlementId);
        Assert.Equal(FulfillmentStatus.Desired, Assert.Single(planned, f => f.Kind == FulfillmentKind.Revoke).Status);
        MappingMigrationTests.AssertGrant(planned, FulfillmentStatus.Confirmed, 1);
        Assert.Equal(1, await MappingMigrationTests.AuditCountAsync(_api, "pacs_instance.grants_migrated"));

        await _api.DrainMessagesAsync(MappingMigrationTests.Tenant);

        var after = await MappingMigrationTests.FulfillmentsAsync(_api, entitlementId);
        var ids = after.Select(f => (FulfillmentId?)f.Id).ToList();
        var commands = (await MappingMigrationTests.CommandsAsync(_api)).Where(c => ids.Contains(c.FulfillmentId)).ToList();
        var revoke = Assert.Single(commands, c => c.Type == ConnectorCommandTypes.RevokeAccess);
        Assert.Equal(oldLevel,
            MappingMigrationTests.Payload<Gatehouse.Connector.Contracts.Commands.RevokeAccess>(revoke).VendorAccessLevelRef);
        var grant = Assert.Single(commands, c => c.Type == ConnectorCommandTypes.GrantAccess && c.Sequence > revoke.Sequence);
        Assert.Equal(MappingMigrationTests.NewLevel,
            MappingMigrationTests.Payload<Gatehouse.Connector.Contracts.Commands.GrantAccess>(grant).VendorAccessLevelRef);
    }

    [Fact]
    public async Task Mapping_delete_keeps_active_grants()
    {
        await MappingMigrationTests.ConnectorModeAsync(_api);
        var (entitlementId, _) = await MappingMigrationTests.ConfirmedConnectorGrantAsync(_api);

        await MappingMigrationTests.EditMappingAsync(_api, null);
        await _api.DrainMessagesAsync(MappingMigrationTests.Tenant);

        var grant = Assert.Single(await MappingMigrationTests.FulfillmentsAsync(_api, entitlementId));
        Assert.Equal((FulfillmentKind.Grant, FulfillmentStatus.Confirmed), (grant.Kind, grant.Status));
        Assert.Equal(0, await MappingMigrationTests.AuditCountAsync(_api, "pacs_instance.grants_migrated"));
    }

    [Fact]
    public async Task Instance_with_fulfillments_cannot_be_deleted()
    {
        var ct = TestContext.Current.CancellationToken;
        Guid pacsId;
        await using (var scope = await _api.CreateTenantScopeAsync("nordlys"))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var instance = await db.PacsInstances.AsNoTracking().FirstAsync(ct);
            pacsId = instance.Id.Value;
            if (!await db.Fulfillments.AnyAsync(f => f.PacsInstanceId == instance.Id, ct))
            {
                var entitlement = await db.Entitlements.AsNoTracking().FirstAsync(ct);
                db.Fulfillments.Add(Gatehouse.Domain.Fulfillment.Fulfillment.Create(entitlement.Id, instance.Id, FulfillmentKind.Grant, CommandPriority.Normal, DateTimeOffset.UtcNow));
                await db.SaveChangesAsync(ct);
            }
        }

        using var freja = _api.CreateClientFor(DemoUser.Freja);
        using var response = await freja.DeleteAsync(new Uri($"/api/v1/admin/pacs-instances/{pacsId}", UriKind.Relative), ct);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("pacs.in_use", await response.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Non_admin_is_403()
    {
        var ct = TestContext.Current.CancellationToken;
        using var anna = _api.CreateClientFor(DemoUser.Anna);

        using var list = await anna.GetAsync(Instances, ct);
        using var create = await anna.PostAsJsonAsync(Instances, new SavePacsInstanceRequest("Nope", PacsVendor.LegacyPacs, [Guid.NewGuid()]), ApiJson.Options, ct);

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    private static async Task<HttpResponseMessage> EnrollAsync(GatewayFactory gateway, string token, CancellationToken ct, string? csrPem = null)
    {
        csrPem ??= ConnectorTestEnrollment.CreateCsr().CsrPem;
        using var client = gateway.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/connector/v1/enroll") { Content = JsonContent.Create(new EnrollRequest(token, csrPem, "1.0.0"), options: ConnectorJson.Options) };
        request.Headers.Add(ConnectorHeaders.ContractVersion, ConnectorHeaders.CurrentContractVersion);
        var response = await client.SendAsync(request, ct);
        await response.Content.LoadIntoBufferAsync(ct);
        return response;
    }

    private static async Task<PacsInstanceAdminDto> CreateAsync(HttpClient client, Guid siteId, CancellationToken ct)
    {
        using var response = await client.PostAsJsonAsync(Instances, new SavePacsInstanceRequest($"PACS {Guid.NewGuid():N}"[..20], PacsVendor.LegacyPacs, [siteId]), ApiJson.Options, ct);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(ct));
        return (await response.Content.ReadFromJsonAsync<PacsInstanceAdminDto>(ApiJson.Options, ct))!;
    }

    private async Task<(Guid SiteId, List<Guid> AreaIds)> SiteWithAreasAsync(CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var rows = await (from a in db.Areas.AsNoTracking()
                          join f in db.Floors.AsNoTracking() on a.FloorId equals f.Id
                          select new { f.SiteId, a.Id }).ToListAsync(ct);
        var site = rows.GroupBy(r => r.SiteId).OrderByDescending(g => g.Count()).First();
        return (site.Key.Value, site.Select(r => r.Id.Value).ToList());
    }

    private async Task<Guid> AreaOutsideAsync(Guid siteId, CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var site = new Gatehouse.Domain.Sites.SiteId(siteId);
        return (await (from a in db.Areas.AsNoTracking()
                       join f in db.Floors.AsNoTracking() on a.FloorId equals f.Id
                       where f.SiteId != site
                       select a.Id).FirstAsync(ct)).Value;
    }
}
