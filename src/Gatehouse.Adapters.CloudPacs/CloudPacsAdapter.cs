using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Adapters;
using Gatehouse.Connector.Contracts.Events;
using Microsoft.Extensions.Logging;

namespace Gatehouse.Adapters.CloudPacs;

/// <summary>CloudPACS REST adapter (adapter-interface.md §5). Never throws for vendor outcomes.</summary>
public sealed class CloudPacsAdapter : IPacsAdapter, IAsyncDisposable
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private readonly CloudPacsAdapterOptions _options;
    private readonly HttpClient _http;
    private readonly ILogger<CloudPacsAdapter> _logger;
    private readonly TimeSpan _timeout;
    private readonly CloudPacsWebhookSubscription _webhook;

    public CloudPacsAdapter(CloudPacsAdapterOptions options, HttpClient http, ILogger<CloudPacsAdapter> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(http);
        _options = options;
        _http = http;
        _logger = logger;
        _timeout = options.RequestTimeout ?? DefaultTimeout;
        _webhook = new CloudPacsWebhookSubscription(options, logger, SendAsync);
    }

    public PacsCapabilities Capabilities { get; } = new(
        "CloudPacs", "1.0.0", NativeValidity: false, PinWrite: true, PhotoWrite: true, PushEvents: true, RecordExport: true,
        [DoorCommandKind.MomentaryUnlock], MaxBatchSize: 100);

    public ValueTask DisposeAsync() => _webhook.DisposeAsync();

    public async Task<AdapterResult> UpsertCardholderAsync(Cardholder cardholder, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cardholder);
        var response = await SendAsync(HttpMethod.Put, $"api/cardholders/{Segment(cardholder.PersonRef)}",
            new { cardholder.GivenName, cardholder.FamilyName, cardholder.CardNumber, cardholder.PhotoUrl }, ct);
        return Log("UpsertCardholder", cardholder.CardNumber, response.Failure ?? AdapterResult.Applied(response.Property("id")));
    }

    public async Task<AdapterResult> GrantAccessAsync(AccessGrant grant, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(grant);
        var response = await SendAsync(HttpMethod.Put,
            $"api/access-groups/{Segment(grant.VendorAccessLevelRef)}/members/{Segment(grant.PersonRef)}", new { grant.CardNumber }, ct);
        return Log("GrantAccess", grant.CardNumber, response.Failure ?? AdapterResult.Applied(response.Property("assignmentId")));
    }

    public async Task<AdapterResult> RevokeAccessAsync(AccessRevoke revoke, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(revoke);
        var response = await SendAsync(HttpMethod.Delete,
            $"api/access-groups/{Segment(revoke.VendorAccessLevelRef)}/members/{Segment(revoke.PersonRef)}", null, ct);
        return Log("RevokeAccess", revoke.CardNumber, response.Failure ?? AdapterResult.Applied(null));
    }

    public async Task<AdapterResult> SetCardStateAsync(CardStateChange change, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(change);
        var state = change.TargetState switch
        {
            CardTargetState.Active => "active",
            CardTargetState.Suspended => "suspended",
            _ => "revoked",
        };
        var response = await SendAsync(
            HttpMethod.Put, $"api/cards/{Segment(change.CardNumber)}", new { ExternalRef = change.PersonRef, State = state }, ct);
        return Log("SetCardState", change.CardNumber, response.Failure ?? AdapterResult.Applied(change.CardNumber));
    }

    public async Task<AdapterResult> SetPinAsync(PinWrite pin, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(pin);
        var response = await SendAsync(HttpMethod.Put, $"api/cardholders/{Segment(pin.PersonRef)}/pin", new { Pin = pin.Pin.Reveal() }, ct);
        return Log("SetPin", pin.CardNumber, response.Failure ?? AdapterResult.Applied(null));
    }

    public async Task<AdapterResult> DoorCommandAsync(DoorCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Kind != DoorCommandKind.MomentaryUnlock)
        {
            return AdapterResult.Unsupported("lockdown");
        }

        foreach (var doorRef in command.PacsDoorRefs)
        {
            var response = await SendAsync(
                HttpMethod.Post, $"api/doors/{Segment(doorRef)}/unlock", new { Seconds = command.DurationSeconds ?? 5 }, ct);
            if (response.Failure is { } failure)
            {
                return Log("MomentaryUnlock", null, failure);
            }
        }

        return Log("MomentaryUnlock", null, AdapterResult.Applied(null));
    }

    /// <summary>
    /// Starts the local webhook listener once, registers it with CloudPACS (retry 1 s → 30 s until success),
    /// then yields pushed events until <paramref name="ct"/> is cancelled.
    /// </summary>
    public async IAsyncEnumerable<PacsDoorEvent> StreamEventsAsync([EnumeratorCancellation] CancellationToken ct)
    {
        CloudPacsWebhookListener listener;
        try
        {
            listener = await _webhook.EnsureAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            yield break;
        }

        while (true)
        {
            try
            {
                if (!await listener.Events.WaitToReadAsync(ct))
                {
                    yield break;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                yield break;
            }

            while (listener.Events.TryRead(out var ev))
            {
                yield return ev;
            }
        }
    }

    /// <summary>
    /// Exports cardholders, cards and assignments by following <c>nextCursor</c> pages.
    /// A vendor failure ends the stream with an <see cref="InvalidOperationException"/> whose message starts with the
    /// mapped error code (e.g. <c>cloudpacs.server_error</c>); the agent's command executor turns it into a failed ack.
    /// </summary>
    public async IAsyncEnumerable<PacsRecord> ExportRecordsAsync(ExportScope scope, [EnumeratorCancellation] CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(scope);
        var levels = scope.VendorAccessLevelRefs is { Count: > 0 } refs ? new HashSet<string>(refs, StringComparer.Ordinal) : null;
        foreach (var type in scope.RecordTypes.Distinct())
        {
            switch (type)
            {
                case RecordType.Cardholder:
                    await foreach (var c in PagesAsync<CloudPacsCardholderItem>("api/cardholders", ct))
                    {
                        yield return new PacsRecord(RecordType.Cardholder, c.Id.ToString(), c.ExternalRef, null, null, null, null, true);
                    }

                    break;
                case RecordType.Card:
                    await foreach (var c in PagesAsync<CloudPacsCardItem>("api/cards", ct))
                    {
                        yield return new PacsRecord(RecordType.Card, c.CardNumber, c.ExternalRef, c.CardNumber, null, null, null,
                            string.Equals(c.State, "active", StringComparison.Ordinal));
                    }

                    break;
                case RecordType.AccessAssignment:
                    await foreach (var a in PagesAsync<CloudPacsAssignmentItem>("api/assignments", ct))
                    {
                        if (levels is null || levels.Contains(a.GroupId))
                        {
                            yield return new PacsRecord(RecordType.AccessAssignment, a.AssignmentId.ToString(), a.ExternalRef, a.CardNumber,
                                a.GroupId, null, null, a.Active);
                        }
                    }

                    break;
            }
        }
    }

    private async IAsyncEnumerable<T> PagesAsync<T>(string path, [EnumeratorCancellation] CancellationToken ct)
    {
        string? cursor = null;
        do
        {
            var query = cursor is null
                ? $"{path}?limit={Capabilities.MaxBatchSize}"
                : $"{path}?limit={Capabilities.MaxBatchSize}&cursor={Segment(cursor)}";
            var response = await SendAsync(HttpMethod.Get, query, null, ct);
            if (response.Failure?.Error is { } error)
            {
                CloudPacsLog.Failed(_logger, "Export", error.Code);
                throw new InvalidOperationException($"{error.Code}: {error.Message}");
            }

            var page = JsonSerializer.Deserialize<CloudPacsPage<T>>(response.Body ?? "{}", Json)
                ?? throw new InvalidOperationException("cloudpacs.rejected: empty export page");
            foreach (var item in page.Items ?? [])
            {
                yield return item;
            }

            cursor = string.IsNullOrEmpty(page.NextCursor) ? null : page.NextCursor;
        }
        while (cursor is not null);
    }

    private static string Segment(string value) => Uri.EscapeDataString(value);

    private AdapterResult Log(string operation, string? card, AdapterResult result)
    {
        if (result.Error is { } error)
        {
            CloudPacsLog.Failed(_logger, operation, error.Code);
        }

        CloudPacsLog.Write(_logger, operation, card is null ? "-" : CardNumberMask.Mask(card), result.Status);
        return result;
    }

    internal async Task<CloudPacsResponse> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_timeout);
        try
        {
            using var request = new HttpRequestMessage(method, new Uri(_options.BaseUrl, path));
            request.Headers.Add("X-Api-Key", _options.ApiKey);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body, options: Json);
            }

            using var response = await _http.SendAsync(request, timeout.Token);
            var text = await response.Content.ReadAsStringAsync(timeout.Token);
            return response.IsSuccessStatusCode
                ? new CloudPacsResponse(text, null)
                : new CloudPacsResponse(text, CloudPacsErrorMapper.FromResponse((int)response.StatusCode, text));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new CloudPacsResponse(null, CloudPacsErrorMapper.Timeout());
        }
        catch (HttpRequestException ex)
        {
            return new CloudPacsResponse(null, CloudPacsErrorMapper.Unreachable(ex.Message));
        }
    }
}
