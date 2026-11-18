using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CloudPacs.Simulator;

/// <summary>Batches events (50 / 200 ms) and posts them, HMAC-signed, to every subscription; queues while none exist.</summary>
public sealed class WebhookDispatcher(
    CloudPacsEvents events, WebhookRegistry registry, IHttpClientFactory clients, TimeProvider time, ILogger<WebhookDispatcher> logger) : BackgroundService
{
    public const int BatchSize = 50;
    public const int MaxPending = 10_000;
    private static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8)];
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string Sign(string secret, byte[] body) =>
        "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pending = new List<SimEvent>();
        while (!stoppingToken.IsCancellationRequested)
        {
            using (var flush = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken))
            {
                flush.CancelAfter(FlushInterval);
                try
                {
                    while (pending.Count < MaxPending && await events.Reader.WaitToReadAsync(flush.Token))
                    {
                        while (events.Reader.TryRead(out var e))
                        {
                            pending.Add(e);
                        }

                        if (pending.Count >= BatchSize)
                        {
                            break;
                        }
                    }
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                }
            }

            if (pending.Count > MaxPending)
            {
                pending.RemoveRange(0, pending.Count - MaxPending);
            }

            var subscriptions = registry.Snapshot();
            if (pending.Count == 0 || subscriptions.Count == 0)
            {
                continue;
            }

            foreach (var chunk in pending.Chunk(BatchSize))
            {
                var body = JsonSerializer.SerializeToUtf8Bytes(chunk, Json);
                foreach (var subscription in subscriptions)
                {
                    _ = DeliverAsync(subscription, body, stoppingToken);
                }
            }

            pending.Clear();
        }
    }

    private async Task DeliverAsync(WebhookSubscription subscription, byte[] body, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, subscription.Url) { Content = new ByteArrayContent(body) };
                request.Content.Headers.ContentType = new("application/json");
                request.Headers.Add("X-CloudPacs-Signature", Sign(subscription.Secret, body));
                using var response = await clients.CreateClient("webhooks").SendAsync(request, ct);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }

                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Webhook {Url} answered {Status} (attempt {Attempt})",
                        subscription.Url, (int)response.StatusCode, attempt + 1);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (HttpRequestException ex)
            {
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Webhook {Url} unreachable: {Reason} (attempt {Attempt})",
                        subscription.Url, ex.Message, attempt + 1);
                }
            }

            if (attempt >= RetryDelays.Length)
            {
                logger.LogWarning("Dropping webhook batch for {Url} after {Attempts} attempts", subscription.Url, attempt + 1);
                return;
            }

            try
            {
                await Task.Delay(RetryDelays[attempt], time, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
