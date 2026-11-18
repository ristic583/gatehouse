using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Events;
using Gatehouse.Connector.Contracts.Protocol;

namespace Gatehouse.Agent.Transport;

public sealed class GatewayClient(HttpClient http, string agentVersion)
{
    public Task<PollResponse> PollAsync(PollRequest request, CancellationToken ct) =>
        PostAsync<PollRequest, PollResponse>("poll", request, ct);

    public Task<AckResponse> AckAsync(AckRequest request, CancellationToken ct) => PostAsync<AckRequest, AckResponse>("acks", request, ct);

    public Task<HeartbeatResponse> HeartbeatAsync(Heartbeat request, CancellationToken ct) =>
        PostAsync<Heartbeat, HeartbeatResponse>("heartbeat", request, ct);

    public Task<DoorEventsResponse> SendEventsAsync(DoorEventsRequest request, CancellationToken ct) =>
        PostAsync<DoorEventsRequest, DoorEventsResponse>("events", request, ct);

    /// <summary><c>POST /connector/v1/exports</c>; 404/409/413 throw <see cref="GatewayRejectedException"/> with the problem
    /// code.</summary>
    public Task SendExportAsync(RecordExport chunk, CancellationToken ct) =>
        PostAsync<RecordExport, JsonElement>("exports", chunk, ct, rejectWithCode: true);

    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        string path, TRequest body, CancellationToken ct, bool rejectWithCode = false)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "connector/v1/" + path)
        {
            Content = JsonContent.Create(body, options: ConnectorJson.Options),
        };
        message.Headers.Add(ConnectorHeaders.ContractVersion, ConnectorHeaders.CurrentContractVersion);
        message.Headers.Add(ConnectorHeaders.AgentVersion, agentVersion);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(message, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new GatewayTransientException("Gateway unreachable", null, ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new GatewayTransientException("Gateway request timed out", null, ex);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new AgentUnauthorizedException((int)response.StatusCode);
            }

            if (rejectWithCode
                && response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict or HttpStatusCode.RequestEntityTooLarge)
            {
                throw new GatewayRejectedException((int)response.StatusCode, await ProblemCodeAsync(response, ct));
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500
                || response.StatusCode == HttpStatusCode.Conflict)
            {
                throw new GatewayTransientException($"Gateway returned {(int)response.StatusCode}", response.Headers.RetryAfter?.Delta);
            }

            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<TResponse>(ConnectorJson.Options, ct))!;
        }
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
