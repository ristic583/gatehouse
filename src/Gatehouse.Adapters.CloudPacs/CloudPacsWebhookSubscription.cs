using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace Gatehouse.Adapters.CloudPacs;

/// <summary>
/// The adapter's push-event subscription: one local <see cref="CloudPacsWebhookListener"/> and one vendor webhook registration
/// (retried with backoff up to 30 s), removed again on dispose.
/// </summary>
internal sealed class CloudPacsWebhookSubscription(
    CloudPacsAdapterOptions options,
    ILogger logger,
    Func<HttpMethod, string, object?, CancellationToken, Task<CloudPacsResponse>> send) : IAsyncDisposable
{
    private static readonly TimeSpan MaxRegistrationDelay = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _streamGate = new(1, 1);
    private CloudPacsWebhookListener? _listener;
    private string? _webhookId;

    public async ValueTask DisposeAsync()
    {
        if (_webhookId is { } id)
        {
            try
            {
                using var unsubscribe = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await send(HttpMethod.Delete, $"api/webhooks/{id}", null, unsubscribe.Token);
            }
            catch (OperationCanceledException)
            {
                // best effort: the vendor drops dead subscriptions after its retries
            }
            catch (ObjectDisposedException)
            {
                // the host already disposed the HttpClient
            }
        }

        if (_listener is not null)
        {
            await _listener.DisposeAsync();
        }

        _streamGate.Dispose();
    }

    public async Task<CloudPacsWebhookListener> EnsureAsync(CancellationToken ct)
    {
        await _streamGate.WaitAsync(ct);
        try
        {
            if (_listener is null)
            {
                var listener = new CloudPacsWebhookListener(
                    options.WebhookListenUrl, Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)), logger);
                await listener.StartAsync(ct);
                _listener = listener;
            }

            if (_webhookId is null)
            {
                var url = options.WebhookPublicUrl
                    ?? new Uri(_listener.BoundAddress!, CloudPacsWebhookListener.Path).ToString();
                _webhookId = await RegisterAsync(url, _listener.Secret, ct);
            }

            return _listener;
        }
        finally
        {
            _streamGate.Release();
        }
    }

    private async Task<string> RegisterAsync(string url, string secret, CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(1);
        while (true)
        {
            var response = await send(HttpMethod.Post, "api/webhooks", new { Url = url, Secret = secret }, ct);
            if (response.Failure?.Error is not { } error && response.Property("id") is { } id)
            {
                CloudPacsLog.WebhookRegistered(logger, id, url);
                return id;
            }

            CloudPacsLog.WebhookRegistrationFailed(logger, response.Failure?.Error?.Code ?? "cloudpacs.rejected", delay);
            await Task.Delay(delay, ct);
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxRegistrationDelay.Ticks));
        }
    }
}
