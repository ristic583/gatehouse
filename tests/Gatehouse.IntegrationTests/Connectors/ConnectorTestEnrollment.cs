using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Adapters;
using Gatehouse.Connector.Contracts.Events;
using Gatehouse.Connector.Contracts.Protocol;
using Gatehouse.Domain.Common;
using Gatehouse.Infrastructure.Connectors;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Connectors;

public static class ConnectorTestEnrollment
{
    public static (string CsrPem, RSA Key) CreateCsr()
    {
        var key = RSA.Create(3072);
        var csr = new CertificateRequest("CN=agent", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return (csr.CreateSigningRequestPem(), key);
    }

    /// <summary>Creates a registration, enrolls through <c>/enroll</c> and returns the issued certificate with its private key.</summary>
    public static async Task<X509Certificate2> EnrollAsync(GatewayFactory factory, string tenant, string pacsName)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var ct = TestContext.Current.CancellationToken;
        string token;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var created = await scope.ServiceProvider.GetRequiredService<ConnectorRegistrationService>().CreateAsync(new TenantId(tenant), pacsName, ct);
            token = created.Value;
        }

        var (csrPem, key) = CreateCsr();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/connector/v1/enroll") { Content = JsonContent.Create(new EnrollRequest(token, csrPem, "1.0.0"), options: ConnectorJson.Options) };
        request.Headers.Add(ConnectorHeaders.ContractVersion, ConnectorHeaders.CurrentContractVersion);
        var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var body = (await response.Content.ReadFromJsonAsync<EnrollResponse>(ConnectorJson.Options, ct))!;
        using var cert = X509Certificate2.CreateFromPem(body.CertificatePem);
        return cert.CopyWithPrivateKey(key);
    }

    public static Task<AsyncServiceScope> ScopeAsync(GatewayFactory factory, string tenant) =>
        factory.Services.GetRequiredService<Gatehouse.Application.Tenancy.ITenantScopeFactory>().CreateScopeAsync(new TenantId(tenant), CancellationToken.None);

    public static Heartbeat NewHeartbeat(bool pinWrite = false) => new(
        "1.0.0",
        new AdapterInfo("LegacyPacs", "1.0.0"),
        new PacsCapabilities("LegacyPacs", "1.0.0", true, pinWrite, false, false, true, [DoorCommandKind.MomentaryUnlock], 50),
        0,
        true,
        DateTimeOffset.UtcNow);

    public static async Task<HttpResponseMessage> PostAsync<T>(HttpClient client, string path, T body, X509Certificate2? cert)
    {
        ArgumentNullException.ThrowIfNull(client);
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body, options: ConnectorJson.Options) };
        request.Headers.Add(ConnectorHeaders.ContractVersion, ConnectorHeaders.CurrentContractVersion);
        if (cert is not null)
        {
            request.Headers.Add(TestClientCertificateStartupFilter.Header, Convert.ToBase64String(cert.RawData));
        }

        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>Restores demo PACS instances (fulfillment mode, heartbeat) and removes registrations after a test.</summary>
    public static async Task ResetAsync(GatewayFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        foreach (var tenant in new[] { "nordlys", "bricks" })
        {
            await using var scope = await factory.Services.GetRequiredService<Gatehouse.Application.Tenancy.ITenantScopeFactory>()
                .CreateScopeAsync(new TenantId(tenant), CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<Gatehouse.Infrastructure.Persistence.Tenant.TenantDbContext>().PacsInstances
                .ExecuteUpdateAsync(p => p
                    .SetProperty(x => x.FulfillmentMode, Gatehouse.Domain.Fulfillment.FulfillmentMode.Manual)
                    .SetProperty(x => x.LastHeartbeatAt, (DateTimeOffset?)null)
                    .SetProperty(x => x.CapabilitiesJson, (string?)null));
        }

        await using var cs = factory.Services.CreateAsyncScope();
        await cs.ServiceProvider.GetRequiredService<Gatehouse.Infrastructure.Persistence.Catalog.CatalogDbContext>().ConnectorRegistrations.ExecuteDeleteAsync(CancellationToken.None);
    }
}
