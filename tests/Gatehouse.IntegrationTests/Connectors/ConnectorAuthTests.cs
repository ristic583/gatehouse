using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Common;
using Gatehouse.Infrastructure.Connectors;
using Gatehouse.Infrastructure.Persistence.Catalog;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Connectors;

public sealed class ConnectorAuthTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatewayFactory _factory = new(postgres);

    public async ValueTask InitializeAsync()
    {
        await _factory.InitializeAsync();
        await ConnectorTestEnrollment.ResetAsync(_factory);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    private static async Task<(HttpStatusCode Status, string? Code)> CodeAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        if (text.Length == 0)
        {
            return (response.StatusCode, null);
        }

        using var doc = JsonDocument.Parse(text);
        return (response.StatusCode, doc.RootElement.TryGetProperty("code", out var c) ? c.GetString() : null);
    }

    [Fact]
    public async Task No_certificate_returns_401()
    {
        using var client = _factory.CreateClient();
        var response = await ConnectorTestEnrollment.PostAsync(client, "/connector/v1/heartbeat", ConnectorTestEnrollment.NewHeartbeat(), null);
        Assert.Equal((HttpStatusCode.Unauthorized, "connector.unauthenticated"), await CodeAsync(response));
    }

    [Fact]
    public async Task Certificate_from_other_ca_returns_401()
    {
        using var key = RSA.Create(3072);
        var req = new CertificateRequest("CN=rogue", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var rogue = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        using var client = _factory.CreateClient();
        var response = await ConnectorTestEnrollment.PostAsync(client, "/connector/v1/heartbeat", ConnectorTestEnrollment.NewHeartbeat(), rogue);
        Assert.Equal((HttpStatusCode.Unauthorized, "connector.unauthenticated"), await CodeAsync(response));
    }

    [Fact]
    public async Task Suspended_tenant_connector_is_403()
    {
        var ct = TestContext.Current.CancellationToken;
        using var cert = await ConnectorTestEnrollment.EnrollAsync(_factory, "nordlys", "HQ-Copenhagen");
        await SetSuspendedAsync(true, ct);
        try
        {
            using var client = _factory.CreateClient();
            var response = await ConnectorTestEnrollment.PostAsync(client, "/connector/v1/heartbeat", ConnectorTestEnrollment.NewHeartbeat(), cert);
            Assert.Equal((HttpStatusCode.Forbidden, "connector.tenant_inactive"), await CodeAsync(response));
        }
        finally
        {
            await SetSuspendedAsync(false, ct);
        }
    }

    private async Task SetSuspendedAsync(bool suspended, CancellationToken ct)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var tenant = await catalog.Tenants.SingleAsync(t => t.Id == new TenantId("nordlys"), ct);
        Assert.True((suspended ? tenant.Suspend("test", DateTimeOffset.UtcNow) : tenant.Reactivate()).IsSuccess);
        await catalog.SaveChangesAsync(ct);
    }

    [Fact]
    public async Task Revoked_registration_returns_403()
    {
        using var cert = await ConnectorTestEnrollment.EnrollAsync(_factory, "nordlys", "HQ-Copenhagen");
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            (await catalog.ConnectorRegistrations.SingleAsync(TestContext.Current.CancellationToken)).Revoke(DateTimeOffset.UtcNow);
            await catalog.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        using var client = _factory.CreateClient();
        var response = await ConnectorTestEnrollment.PostAsync(client, "/connector/v1/heartbeat", ConnectorTestEnrollment.NewHeartbeat(), cert);
        Assert.Equal((HttpStatusCode.Forbidden, "connector.revoked"), await CodeAsync(response));
    }

    [Fact]
    public async Task Rejects_cert_for_another_tenant_or_instance()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = _factory.Services.CreateAsyncScope();
        var registrations = scope.ServiceProvider.GetRequiredService<ConnectorRegistrationService>();
        await registrations.CreateAsync(new TenantId("nordlys"), "HQ-Copenhagen", ct);
        var catalog = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var registration = await catalog.ConnectorRegistrations.SingleAsync(ct);

        // Certificate issued with a SAN for bricks, then bound to the nordlys registration.
        var (csr, key) = ConnectorTestEnrollment.CreateCsr();
        var issued = await scope.ServiceProvider.GetRequiredService<ConnectorCertificateAuthority>()
            .IssueClientCertificateAsync(csr, new TenantId("bricks"), registration.PacsInstanceId, registration.Id, DateTimeOffset.UtcNow, ct);
        registration.Enroll(registration.EnrollmentTokenHash!, issued.Value.ThumbprintSha256Hex, issued.Value.NotAfter, DateTimeOffset.UtcNow);
        await catalog.SaveChangesAsync(ct);
        using var cert = X509Certificate2.CreateFromPem(issued.Value.CertificatePem).CopyWithPrivateKey(key);

        using var client = _factory.CreateClient();
        var response = await ConnectorTestEnrollment.PostAsync(client, "/connector/v1/heartbeat", ConnectorTestEnrollment.NewHeartbeat(), cert);
        Assert.Equal((HttpStatusCode.Forbidden, "connector.scope_mismatch"), await CodeAsync(response));
    }
}
