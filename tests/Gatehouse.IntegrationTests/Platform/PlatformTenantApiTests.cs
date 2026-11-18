using System.Net;
using System.Net.Http.Json;
using Gatehouse.Application.Platform.Dtos;
using Gatehouse.Application.Platform.Provisioning;
using Gatehouse.Domain.Platform;
using Gatehouse.Domain.Tenancy;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Platform;

/// <summary>authorization.md §11 platform routes.</summary>
public sealed class PlatformTenantApiTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly Uri AnnaMe = new("/api/v1/me", UriKind.Relative);
    private readonly GatehouseApiFactory _api = new(postgres);

    public ValueTask InitializeAsync() => _api.InitializeAsync();

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Create_tenant_returns_202_and_starts_provisioning()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await PlatformTestData.OperatorClientAsync(_api, ct);
        var request = PlatformTestData.NewTenant();

        using var response = await PlatformTestData.CreateAsync(client, request, ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var dto = (await response.Content.ReadFromJsonAsync<PlatformTenantDto>(ApiJson.Options, ct))!;
        Assert.Equal(request.TenantId, dto.TenantId);
        Assert.Equal(TenantStatus.Provisioning, dto.Status);
        Assert.Equal(ProvisioningStep.Database, dto.Provisioning!.Step);
        Assert.Equal(ProvisioningState.Running, dto.Provisioning.State);

        await _api.Services.GetRequiredService<ITenantProvisioningRunner>().RunOnceAsync(ct);
        var after = (await client.GetFromJsonAsync<PlatformTenantDto>(new Uri($"/api/v1/platform/tenants/{request.TenantId}", UriKind.Relative), ApiJson.Options, ct))!;
        Assert.Equal(TenantStatus.Active, after.Status);
        var list = (await client.GetFromJsonAsync<List<PlatformTenantSummaryDto>>(PlatformTestData.Tenants, ApiJson.Options, ct))!;
        Assert.Contains(list, t => t.TenantId == request.TenantId);
    }

    [Fact]
    public async Task Duplicate_tenant_id_is_409()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await PlatformTestData.OperatorClientAsync(_api, ct);
        var request = PlatformTestData.NewTenant() with { TenantId = "nordlys" };

        using var response = await PlatformTestData.CreateAsync(client, request, ct);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "tenant.already_exists", ct);
    }

    [Fact]
    public async Task Taken_email_domain_is_409()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await PlatformTestData.OperatorClientAsync(_api, ct);
        var first = PlatformTestData.NewTenant();
        using var created = await PlatformTestData.CreateAsync(client, first, ct);
        Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);

        using var response = await PlatformTestData.CreateAsync(client, PlatformTestData.NewTenant(first.EmailDomain), ct);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "tenant.email_domain_taken", ct);
    }

    [Fact]
    public async Task Admin_email_outside_domain_is_400()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await PlatformTestData.OperatorClientAsync(_api, ct);
        var request = PlatformTestData.NewTenant() with { AdminEmail = "someone@elsewhere.example" };

        using var response = await PlatformTestData.CreateAsync(client, request, ct);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "validation.failed", ct);
    }

    [Fact]
    public async Task Suspend_blocks_tenant_users()
    {
        var ct = TestContext.Current.CancellationToken;
        using var platform = await PlatformTestData.OperatorClientAsync(_api, ct);
        using var anna = _api.CreateClientFor(DemoUser.Anna);
        using (var before = await anna.GetAsync(AnnaMe, ct))
        {
            Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        }

        try
        {
            using var suspend = await platform.PostAsJsonAsync(new Uri("/api/v1/platform/tenants/nordlys/suspend", UriKind.Relative), new SuspendTenantRequest("Unpaid invoice"), ApiJson.Options, ct);
            Assert.Equal(HttpStatusCode.OK, suspend.StatusCode);
            var dto = (await suspend.Content.ReadFromJsonAsync<PlatformTenantDto>(ApiJson.Options, ct))!;
            Assert.Equal(TenantStatus.Suspended, dto.Status);
            Assert.Equal("Unpaid invoice", dto.SuspendedReason);
            Assert.False(_api.Services.GetRequiredService<FakeIdentityProvisioner>().Organizations.TryGetValue(new("nordlys"), out var org) && org.Enabled);

            using var after = await anna.GetAsync(AnnaMe, ct);
            Assert.Equal(HttpStatusCode.Forbidden, after.StatusCode);
        }
        finally
        {
            using var _ = await platform.PostAsync(new Uri("/api/v1/platform/tenants/nordlys/reactivate", UriKind.Relative), null, ct);
        }
    }

    [Fact]
    public async Task Reactivate_restores_access()
    {
        var ct = TestContext.Current.CancellationToken;
        using var platform = await PlatformTestData.OperatorClientAsync(_api, ct);
        using var anna = _api.CreateClientFor(DemoUser.Anna);
        using var suspend = await platform.PostAsJsonAsync(new Uri("/api/v1/platform/tenants/nordlys/suspend", UriKind.Relative), new SuspendTenantRequest("Maintenance"), ApiJson.Options, ct);
        Assert.Equal(HttpStatusCode.OK, suspend.StatusCode);

        using var reactivate = await platform.PostAsync(new Uri("/api/v1/platform/tenants/nordlys/reactivate", UriKind.Relative), null, ct);

        Assert.Equal(HttpStatusCode.OK, reactivate.StatusCode);
        using var me = await anna.GetAsync(AnnaMe, ct);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var again = await platform.PostAsync(new Uri("/api/v1/platform/tenants/nordlys/reactivate", UriKind.Relative), null, ct);
        await AssertProblemAsync(again, HttpStatusCode.Conflict, "tenant.invalid_status_transition", ct);
    }

    [Fact]
    public async Task Retry_only_when_failed()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await PlatformTestData.OperatorClientAsync(_api, ct);
        var request = PlatformTestData.NewTenant();
        using var created = await PlatformTestData.CreateAsync(client, request, ct);
        var retryUri = new Uri($"/api/v1/platform/tenants/{request.TenantId}/provisioning/retry", UriKind.Relative);

        using var early = await client.PostAsync(retryUri, null, ct);
        await AssertProblemAsync(early, HttpStatusCode.Conflict, "tenant.provisioning_not_failed", ct);

        _api.Services.GetRequiredService<FakeIdentityProvisioner>().FailNext(nameof(FakeIdentityProvisioner.EnsureOrganizationAsync));
        var runner = _api.Services.GetRequiredService<ITenantProvisioningRunner>();
        await runner.RunOnceAsync(ct);

        using var retry = await client.PostAsync(retryUri, null, ct);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var dto = (await retry.Content.ReadFromJsonAsync<PlatformTenantDto>(ApiJson.Options, ct))!;
        Assert.Equal(ProvisioningState.Running, dto.Provisioning!.State);
        Assert.Equal(ProvisioningStep.Organization, dto.Provisioning.Step);
        await runner.RunOnceAsync(ct);
        using var unknown = await client.PostAsync(new Uri("/api/v1/platform/tenants/no-such-tenant/provisioning/retry", UriKind.Relative), null, ct);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task Every_write_is_platform_audited()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await PlatformTestData.OperatorClientAsync(_api, ct);
        var request = PlatformTestData.NewTenant();
        using var created = await PlatformTestData.CreateAsync(client, request, ct);
        await _api.Services.GetRequiredService<ITenantProvisioningRunner>().RunOnceAsync(ct);
        var basePath = $"/api/v1/platform/tenants/{request.TenantId}";
        using var invite = await client.PostAsync(new Uri($"{basePath}/admin-invite", UriKind.Relative), null, ct);
        Assert.Equal(HttpStatusCode.NoContent, invite.StatusCode);
        using var suspend = await client.PostAsJsonAsync(new Uri($"{basePath}/suspend", UriKind.Relative), new SuspendTenantRequest("Audit check"), ApiJson.Options, ct);
        using var reactivate = await client.PostAsync(new Uri($"{basePath}/reactivate", UriKind.Relative), null, ct);

        var actions = await PlatformTestData.AuditActionsAsync(_api, request.TenantId, ct);
        Assert.Equal(
            ["platform.tenant_created", "platform.admin_invite_sent", "platform.tenant_provisioned", "platform.admin_invite_sent", "platform.tenant_suspended", "platform.tenant_reactivated"],
            actions);
        var audit = (await client.GetFromJsonAsync<List<PlatformAuditEntryDto>>(new Uri($"/api/v1/platform/audit?tenantId={request.TenantId}&limit=2", UriKind.Relative), ApiJson.Options, ct))!;
        Assert.Equal(["platform.tenant_reactivated", "platform.tenant_suspended"], audit.Select(a => a.Action));
        Assert.All(audit, a => Assert.Equal("Operator", a.ActorKind));
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code, CancellationToken ct)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Contains(code, await response.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }
}
