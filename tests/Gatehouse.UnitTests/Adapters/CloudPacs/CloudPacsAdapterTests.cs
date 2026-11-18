using System.Net;
using System.Text;
using Gatehouse.Adapters.CloudPacs;
using Gatehouse.Connector.Contracts.Adapters;
using Microsoft.Extensions.Logging;

namespace Gatehouse.UnitTests.Adapters.CloudPacs;

public sealed class CloudPacsAdapterTests : IDisposable
{
    private readonly StubHandler _handler = new();
    private readonly ListLogger _logger = new();
    private readonly HttpClient _http;
    private readonly CloudPacsAdapter _adapter;

    public CloudPacsAdapterTests()
    {
        _http = new HttpClient(_handler);
        _adapter = new CloudPacsAdapter(
            new CloudPacsAdapterOptions(new Uri("http://cloudpacs/"), "key", "http://127.0.0.1:0", RequestTimeout: TimeSpan.FromMilliseconds(200)),
            _http, _logger);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AccessGrant Grant(string level = "AG-AAR-DOCK") =>
        new("p1", "123456789", level, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddDays(1));

    public void Dispose()
    {
        _http.Dispose();
        _handler.Dispose();
    }

    [Fact]
    public void Capabilities_declare_no_native_validity_and_no_lockdown()
    {
        var c = _adapter.Capabilities;
        Assert.Equal("CloudPacs", c.Vendor);
        Assert.False(c.NativeValidity);
        Assert.True(c.PinWrite);
        Assert.True(c.PushEvents);
        Assert.Equal([DoorCommandKind.MomentaryUnlock], c.DoorCommands);
    }

    [Fact]
    public async Task Grant_puts_membership_and_returns_assignment_ref()
    {
        _handler.Respond(HttpStatusCode.OK, """{"assignmentId":"a-1"}""");
        var result = await _adapter.GrantAccessAsync(Grant(), Ct);
        Assert.Equal(AdapterStatus.Applied, result.Status);
        Assert.Equal("a-1", result.PacsRef);
        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("/api/access-groups/AG-AAR-DOCK/members/p1", request.Path);
        Assert.Equal("key", request.ApiKey);
    }

    [Fact]
    public async Task Rate_limited_is_retryable_failure()
    {
        _handler.Respond(HttpStatusCode.TooManyRequests, """{"error":"rate_limited"}""");
        var result = await _adapter.GrantAccessAsync(Grant(), Ct);
        Assert.Equal("cloudpacs.rate_limited", result.Error!.Code);
        Assert.True(result.Error.Retryable);
    }

    [Fact]
    public async Task Server_error_is_retryable_failure()
    {
        _handler.Respond(HttpStatusCode.ServiceUnavailable, """{"error":"unavailable"}""");
        var result = await _adapter.UpsertCardholderAsync(new Cardholder("p1", "A", "B", "1", null), Ct);
        Assert.Equal(AdapterStatus.Failed, result.Status);
        Assert.Equal("cloudpacs.server_error", result.Error!.Code);
        Assert.True(result.Error.Retryable);
    }

    [Fact]
    public async Task Unknown_access_group_is_non_retryable()
    {
        _handler.Respond(HttpStatusCode.NotFound, """{"error":"unknown_access_group"}""");
        var result = await _adapter.GrantAccessAsync(Grant("AG-NOPE"), Ct);
        Assert.Equal("cloudpacs.unknown_access_level", result.Error!.Code);
        Assert.False(result.Error.Retryable);
    }

    [Fact]
    public async Task Timeout_is_retryable_failure()
    {
        _handler.Delay = TimeSpan.FromSeconds(5);
        var result = await _adapter.RevokeAccessAsync(new AccessRevoke("p1", "1", "AG-AAR-DOCK"), Ct);
        Assert.Equal("cloudpacs.timeout", result.Error!.Code);
        Assert.True(result.Error.Retryable);
    }

    [Fact]
    public async Task Lockdown_is_unsupported_without_http_call()
    {
        var result = await _adapter.DoorCommandAsync(new DoorCommand(DoorCommandKind.Lockdown, ["D-AAR-DOCK"], null, LockdownAction.Start, "r"), Ct);
        Assert.Equal(AdapterStatus.Unsupported, result.Status);
        Assert.Equal("adapter.capability_missing", result.Error!.Code);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task Set_pin_sends_pin_and_never_logs_it()
    {
        _handler.Respond(HttpStatusCode.NoContent, string.Empty);
        var result = await _adapter.SetPinAsync(new PinWrite("p1", "123456789", new SensitivePin("4711")), Ct);
        Assert.Equal(AdapterStatus.Applied, result.Status);
        var request = Assert.Single(_handler.Requests);
        Assert.Equal("/api/cardholders/p1/pin", request.Path);
        Assert.Contains("4711", request.Body, StringComparison.Ordinal);
        Assert.NotEmpty(_logger.Messages);
        Assert.DoesNotContain(_logger.Messages, m => m.Contains("4711", StringComparison.Ordinal) || m.Contains("123456789", StringComparison.Ordinal));
    }

    private sealed record Captured(HttpMethod Method, string Path, string? ApiKey, string Body);

    private sealed class StubHandler : HttpMessageHandler
    {
        private HttpStatusCode _status = HttpStatusCode.OK;
        private string _body = "{}";

        public List<Captured> Requests { get; } = [];

        public TimeSpan Delay { get; set; }

        public void Respond(HttpStatusCode status, string body) => (_status, _body) = (status, body);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new Captured(request.Method, request.RequestUri!.AbsolutePath, request.Headers.GetValues("X-Api-Key").FirstOrDefault(), body));
            if (Delay > TimeSpan.Zero)
            {
                await Task.Delay(Delay, cancellationToken);
            }

            return new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class ListLogger : ILogger<CloudPacsAdapter>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
