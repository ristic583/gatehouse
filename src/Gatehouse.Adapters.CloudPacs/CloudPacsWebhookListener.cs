using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Gatehouse.Connector.Contracts.Adapters;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Gatehouse.Adapters.CloudPacs;

/// <summary>
/// Local HTTP listener for CloudPACS webhooks (adapter-interface.md §5): verifies
/// <c>X-CloudPacs-Signature</c> and writes mapped events to a bounded channel.
/// </summary>
internal sealed class CloudPacsWebhookListener : IAsyncDisposable
{
    public const string Path = "/webhooks/cloudpacs";
    public const string SignatureHeader = "X-CloudPacs-Signature";
    private const int Capacity = 10_000;

    private readonly string _listenUrl;
    private readonly byte[] _secretKey;
    private readonly ILogger _logger;
    private readonly Channel<PacsDoorEvent> _events = Channel.CreateBounded<PacsDoorEvent>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
    private WebApplication? _app;

    public CloudPacsWebhookListener(string listenUrl, string secret, ILogger logger)
    {
        _listenUrl = listenUrl;
        Secret = secret;
        _secretKey = Encoding.UTF8.GetBytes(secret);
        _logger = logger;
    }

    /// <summary>Shared HMAC secret registered with the vendor.</summary>
    public string Secret { get; }

    public ChannelReader<PacsDoorEvent> Events => _events.Reader;

    /// <summary>Base address the listener is bound to (resolved port when listening on port 0).</summary>
    public Uri? BoundAddress { get; private set; }

    public async Task<Uri> StartAsync(CancellationToken ct)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls(_listenUrl);
        var app = builder.Build();
        Func<HttpContext, Task<IResult>> handler = ReceiveAsync;
        app.MapPost(Path, handler);
        await app.StartAsync(ct);
        _app = app;
        var address = app.Urls.First();
        BoundAddress = new Uri(address);
        CloudPacsLog.ListenerStarted(_logger, address);
        return BoundAddress;
    }

    public async ValueTask DisposeAsync()
    {
        _events.Writer.TryComplete();
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
            _app = null;
        }
    }

    private async Task<IResult> ReceiveAsync(HttpContext context)
    {
        using var buffer = new MemoryStream();
        await context.Request.Body.CopyToAsync(buffer, context.RequestAborted);
        var body = buffer.ToArray();
        if (!SignatureMatches(context.Request.Headers[SignatureHeader].ToString(), body))
        {
            CloudPacsLog.BadSignature(_logger);
            return Results.Unauthorized();
        }

        CloudPacsWebhookEvent[]? payload;
        try
        {
            payload = JsonSerializer.Deserialize<CloudPacsWebhookEvent[]>(body, CloudPacsAdapter.Json);
        }
        catch (JsonException)
        {
            CloudPacsLog.BadPayload(_logger);
            return Results.BadRequest();
        }

        foreach (var vendorEvent in payload ?? [])
        {
            if (CloudPacsEventMapper.Map(vendorEvent) is { } mapped)
            {
                await _events.Writer.WriteAsync(mapped, context.RequestAborted);
            }
            else
            {
                CloudPacsLog.UnknownEventType(_logger, vendorEvent.Type);
            }
        }

        return Results.NoContent();
    }

    private bool SignatureMatches(string header, byte[] body)
    {
        const string Prefix = "sha256=";
        if (!header.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        byte[] provided;
        try
        {
            provided = Convert.FromHexString(header.AsSpan(Prefix.Length));
        }
        catch (FormatException)
        {
            return false;
        }

        var expected = HMACSHA256.HashData(_secretKey, body);
        return CryptographicOperations.FixedTimeEquals(expected, provided);
    }
}
