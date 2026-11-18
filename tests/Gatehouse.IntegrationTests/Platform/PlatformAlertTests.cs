using System.Net.Http.Json;
using Gatehouse.Application.Platform.Dtos;
using Gatehouse.Application.Platform.Provisioning;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.Platform;
using Gatehouse.Infrastructure.Persistence.Catalog;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Platform;

/// <summary><c>domain-model.md</c> §1 platform alerts (computed on read).</summary>
public sealed class PlatformAlertTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly Uri Alerts = new("/api/v1/platform/alerts", UriKind.Relative);
    private readonly GatehouseApiFactory _api = new(postgres);

    public ValueTask InitializeAsync() => _api.InitializeAsync();

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Failed_provisioning_raises_alert()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = await PlatformTestData.OperatorClientAsync(_api, ct);
        var request = PlatformTestData.NewTenant();
        using var created = await PlatformTestData.CreateAsync(client, request, ct);
        _api.Services.GetRequiredService<FakeIdentityProvisioner>().FailNext(nameof(FakeIdentityProvisioner.InviteTenantAdminAsync));
        await _api.Services.GetRequiredService<ITenantProvisioningRunner>().RunOnceAsync(ct);

        var alerts = await AlertsAsync(client, ct);

        var alert = Assert.Single(alerts, a => a.TenantId == request.TenantId && a.Kind == PlatformAlertKind.ProvisioningFailed);
        Assert.Equal(AlertSeverity.Error, alert.Severity);
        Assert.Contains("AdminInvite", alert.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Offline_connector_raises_alert()
    {
        var ct = TestContext.Current.CancellationToken;
        await using (var scope = _api.Services.CreateAsyncScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            catalog.TenantStatistics.Add(new TenantStatistics { TenantId = new TenantId("bricks"), ConnectorsOffline = 2, ComputedAt = DateTimeOffset.UtcNow });
            await catalog.SaveChangesAsync(ct);
        }

        using var client = await PlatformTestData.OperatorClientAsync(_api, ct);
        var alerts = await AlertsAsync(client, ct);

        var alert = Assert.Single(alerts, a => a.TenantId == "bricks" && a.Kind == PlatformAlertKind.ConnectorOffline);
        Assert.Equal(AlertSeverity.Warning, alert.Severity);
        var summary = (await client.GetFromJsonAsync<List<PlatformTenantSummaryDto>>(PlatformTestData.Tenants, ApiJson.Options, ct))!.Single(t => t.TenantId == "bricks");
        Assert.Equal(2, summary.ConnectorsOffline);
        Assert.True(summary.AlertCount >= 1);
    }

    [Fact]
    public async Task Expiring_certificate_raises_alert()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        await using (var scope = _api.Services.CreateAsyncScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            var registration = ConnectorRegistration.Create(new TenantId("nordlys"), PacsInstanceId.New(), "Expiring agent", "token-hash-" + Guid.NewGuid().ToString("N"), now.AddDays(-30));
            Assert.True(registration.Enroll(registration.EnrollmentTokenHash!, "thumb-" + Guid.NewGuid().ToString("N"), now.AddDays(5), now.AddDays(-30)).IsSuccess);
            catalog.ConnectorRegistrations.Add(registration);
            await catalog.SaveChangesAsync(ct);
        }

        using var client = await PlatformTestData.OperatorClientAsync(_api, ct);
        var alerts = await AlertsAsync(client, ct);

        var alert = Assert.Single(alerts, a => a.TenantId == "nordlys" && a.Kind == PlatformAlertKind.CertificateExpiring);
        Assert.Equal(AlertSeverity.Warning, alert.Severity);
        var tenant = (await client.GetFromJsonAsync<PlatformTenantDto>(new Uri("/api/v1/platform/tenants/nordlys", UriKind.Relative), ApiJson.Options, ct))!;
        Assert.Contains(tenant.Connectors, c => c.DisplayName == "Expiring agent" && c.Status == ConnectorStatus.Active);
    }

    private static async Task<List<PlatformAlertDto>> AlertsAsync(HttpClient client, CancellationToken ct) =>
        (await client.GetFromJsonAsync<List<PlatformAlertDto>>(Alerts, ApiJson.Options, ct))!;
}
