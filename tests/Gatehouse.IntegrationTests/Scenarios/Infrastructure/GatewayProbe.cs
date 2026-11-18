using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Events;
using Gatehouse.Connector.Contracts.Protocol;

namespace Gatehouse.IntegrationTests.Scenarios.Infrastructure;

/// <summary>
/// Talks to the connector gateway as the HQ agent (its enrolled client certificate from the agent data directory, the same path the
/// AppHost gives <c>agent-nordlys-hq</c>), so a door-event batch can be delivered twice or an older event late.
/// </summary>
public sealed class GatewayProbe : IDisposable
{
    private readonly HttpClient _http;
    private readonly X509Certificate2 _certificate;

    public GatewayProbe(Uri gatewayUrl)
    {
        var pfx = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gatehouse", "agents", "nordlys-hq-copenhagen", "client.pfx");
        _certificate = X509CertificateLoader.LoadPkcs12FromFile(pfx, null);
#pragma warning disable CA2000 // The handler is owned and disposed by the HttpClient.
        var handler = new HttpClientHandler
        {
            // The AppHost gateway uses the dev certificate; the agents trust it the same way (Agent__TrustGatewayCertificate=dev).
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
#pragma warning restore CA2000
        handler.ClientCertificates.Add(_certificate);
        _http = new HttpClient(handler, disposeHandler: true) { BaseAddress = gatewayUrl };
    }

    public async Task<DoorEventsResponse> PostDoorEventsAsync(IReadOnlyList<DoorEvent> events, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri("/connector/v1/events", UriKind.Relative))
        {
            Content = JsonContent.Create(new DoorEventsRequest(events), options: ConnectorJson.Options),
        };
        request.Headers.Add(ConnectorHeaders.ContractVersion, ConnectorHeaders.CurrentContractVersion);
        request.Headers.Add(ConnectorHeaders.AgentVersion, "m11-scenario");
        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"POST /connector/v1/events: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(ct)}");
        }

        return (await response.Content.ReadFromJsonAsync<DoorEventsResponse>(ConnectorJson.Options, ct))!;
    }

    public void Dispose()
    {
        _http.Dispose();
        _certificate.Dispose();
    }
}
