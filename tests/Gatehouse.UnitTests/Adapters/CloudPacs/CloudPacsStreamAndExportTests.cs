using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Gatehouse.Adapters.CloudPacs;
using Gatehouse.Connector.Contracts.Adapters;
using Gatehouse.Connector.Contracts.Events;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gatehouse.UnitTests.Adapters.CloudPacs;

public sealed class CloudPacsStreamAndExportTests : IAsyncDisposable
{
    private readonly RouteHandler _handler = new();
    private readonly HttpClient _http;
    private readonly HttpClient _sender = new();
    private readonly CloudPacsAdapter _adapter;

    public CloudPacsStreamAndExportTests()
    {
        _http = new HttpClient(_handler);
        _adapter = new CloudPacsAdapter(
            new CloudPacsAdapterOptions(new Uri("http://cloudpacs/"), "key", "http://127.0.0.1:0"),
            _http, NullLogger<CloudPacsAdapter>.Instance);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await _adapter.DisposeAsync();
        _http.Dispose();
        _sender.Dispose();
        _handler.Dispose();
    }

    [Fact]
    public async Task Export_follows_cursor_pages()
    {
        _handler.Route = request => request.RequestUri!.Query.Contains("cursor=2", StringComparison.Ordinal)
            ? (HttpStatusCode.OK, """{"items":[{"id":"00000000-0000-0000-0000-000000000003","externalRef":"p3"}],"nextCursor":null}""")
            : (HttpStatusCode.OK, """{"items":[{"id":"00000000-0000-0000-0000-000000000001","externalRef":"p1"},{"id":"00000000-0000-0000-0000-000000000002","externalRef":"p2"}],"nextCursor":"2"}""");

        var records = await ToListAsync(_adapter.ExportRecordsAsync(new ExportScope([RecordType.Cardholder], null), Ct));

        Assert.Equal(["p1", "p2", "p3"], records.Select(r => r.PersonRef));
        Assert.All(records, r => Assert.True(r.IsActive));
        Assert.Equal(2, _handler.Requests.Count);
        Assert.All(_handler.Requests, r => Assert.Contains("limit=100", r.Query, StringComparison.Ordinal));
        Assert.All(_handler.Requests, r => Assert.Equal("/api/cardholders", r.Path));
    }

    [Fact]
    public async Task Export_marks_removed_assignments_inactive()
    {
        _handler.Route = _ => (HttpStatusCode.OK, """
            {"items":[
              {"assignmentId":"00000000-0000-0000-0000-00000000000a","groupId":"AG-AAR-DOCK","externalRef":"p1","cardNumber":"111","active":true},
              {"assignmentId":"00000000-0000-0000-0000-00000000000b","groupId":"AG-AAR-DOCK","externalRef":"p2","cardNumber":"222","active":false},
              {"assignmentId":"00000000-0000-0000-0000-00000000000c","groupId":"AG-OTHER","externalRef":"p3","cardNumber":"333","active":true}
            ],"nextCursor":null}
            """);

        var records = await ToListAsync(_adapter.ExportRecordsAsync(new ExportScope([RecordType.AccessAssignment], ["AG-AAR-DOCK"]), Ct));

        Assert.Equal(2, records.Count);
        var removed = Assert.Single(records, r => r.PersonRef == "p2");
        Assert.False(removed.IsActive);
        Assert.Equal("00000000-0000-0000-0000-00000000000b", removed.PacsRef);
        Assert.Equal("AG-AAR-DOCK", removed.VendorAccessLevelRef);
        Assert.Null(removed.ValidFrom);
        Assert.Null(removed.ValidTo);
        Assert.True(Assert.Single(records, r => r.PersonRef == "p1").IsActive);
    }

    [Fact]
    public async Task Webhook_with_valid_signature_yields_event()
    {
        await using var stream = await StartStreamAsync();
        var id = Guid.CreateVersion7();

        var status = await PostAsync(stream, Payload((id, "access.granted")), stream.Secret);

        Assert.Equal(HttpStatusCode.NoContent, status);
        var ev = await stream.NextAsync();
        Assert.Equal(id, ev.EventId);
        Assert.Equal(DoorEventType.AccessGranted, ev.Type);
        Assert.Equal("D-AAR-DOCK", ev.PacsDoorRef);
        Assert.Equal("p1", ev.PersonRef);
        Assert.Equal(ReaderDirection.In, ev.Direction);
    }

    [Fact]
    public async Task Webhook_with_bad_signature_is_401_and_dropped()
    {
        await using var stream = await StartStreamAsync();
        var forged = Guid.CreateVersion7();
        var genuine = Guid.CreateVersion7();

        var forgedStatus = await PostAsync(stream, Payload((forged, "door.forced")), "not-the-secret");
        var genuineStatus = await PostAsync(stream, Payload((genuine, "door.opened")), stream.Secret);

        Assert.Equal(HttpStatusCode.Unauthorized, forgedStatus);
        Assert.Equal(HttpStatusCode.NoContent, genuineStatus);
        Assert.Equal(genuine, (await stream.NextAsync()).EventId);
    }

    [Fact]
    public async Task Unknown_vendor_event_type_is_skipped()
    {
        await using var stream = await StartStreamAsync();
        var unknown = Guid.CreateVersion7();
        var known = Guid.CreateVersion7();

        var status = await PostAsync(stream, Payload((unknown, "door.teleported"), (known, "door.closed")), stream.Secret);

        Assert.Equal(HttpStatusCode.NoContent, status);
        var ev = await stream.NextAsync();
        Assert.Equal(known, ev.EventId);
        Assert.Equal(DoorEventType.DoorClosed, ev.Type);
    }

    private async Task<EventStream> StartStreamAsync()
    {
        _handler.Route = request => request.RequestUri!.AbsolutePath == "/api/webhooks"
            ? (HttpStatusCode.OK, """{"id":"6f1b0c9e-0000-0000-0000-000000000001"}""")
            : (HttpStatusCode.NotFound, """{"error":"not_found"}""");
        var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        var enumerator = _adapter.StreamEventsAsync(cts.Token).GetAsyncEnumerator(cts.Token);
        var first = enumerator.MoveNextAsync();

        var deadline = DateTime.UtcNow.AddSeconds(10);
        Captured? registration = null;
        while (registration is null && DateTime.UtcNow < deadline)
        {
            registration = _handler.Requests.FirstOrDefault(r => r.Path == "/api/webhooks");
            if (registration is null)
            {
                await Task.Delay(20, Ct);
            }
        }

        Assert.NotNull(registration);
        using var body = JsonDocument.Parse(registration.Body);
        var url = new Uri(body.RootElement.GetProperty("url").GetString()!);
        var secret = body.RootElement.GetProperty("secret").GetString()!;
        Assert.Equal("/webhooks/cloudpacs", url.AbsolutePath);
        Assert.Equal(64, secret.Length);
        return new EventStream(enumerator, first, url, secret, cts);
    }

    private async Task<HttpStatusCode> PostAsync(EventStream stream, string payload, string secret)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        using var request = new HttpRequestMessage(HttpMethod.Post, stream.Url) { Content = new ByteArrayContent(bytes) };
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.Add("X-CloudPacs-Signature",
            "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), bytes)));
        using var response = await _sender.SendAsync(request, Ct);
        return response.StatusCode;
    }

    private static string Payload(params (Guid Id, string Type)[] events) => JsonSerializer.Serialize(
        events.Select((e, i) => new
        {
            id = e.Id,
            sequence = i + 1,
            type = e.Type,
            doorRef = "D-AAR-DOCK",
            readerRef = "R-AAR-DOCK-IN",
            cardNumber = "123456789",
            externalRef = "p1",
            direction = "In",
            occurredAt = DateTimeOffset.UnixEpoch,
        }));

    private static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source)
        {
            list.Add(item);
        }

        return list;
    }

    private sealed class EventStream(
        IAsyncEnumerator<PacsDoorEvent> enumerator, ValueTask<bool> first, Uri url, string secret, CancellationTokenSource cts) : IAsyncDisposable
    {
        private ValueTask<bool>? _pending = first;

        public Uri Url { get; } = url;

        public string Secret { get; } = secret;

        public async Task<PacsDoorEvent> NextAsync()
        {
            var moved = await (_pending ?? enumerator.MoveNextAsync());
            _pending = null;
            Assert.True(moved);
            return enumerator.Current;
        }

        public async ValueTask DisposeAsync()
        {
            await cts.CancelAsync();
            if (_pending is { } pending)
            {
                await pending;
            }

            await enumerator.DisposeAsync();
            cts.Dispose();
        }
    }

    private sealed record Captured(string Path, string Query, string Body);

    private sealed class RouteHandler : HttpMessageHandler
    {
        private readonly Lock _gate = new();
        private readonly List<Captured> _requests = [];

        public Func<HttpRequestMessage, (HttpStatusCode Status, string Body)> Route { get; set; } =
            _ => (HttpStatusCode.NotFound, "{}");

        public IReadOnlyList<Captured> Requests
        {
            get
            {
                lock (_gate)
                {
                    return [.. _requests];
                }
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (_gate)
            {
                _requests.Add(new Captured(request.RequestUri!.AbsolutePath, request.RequestUri.Query, body));
            }

            var (status, text) = Route(request);
            return new HttpResponseMessage(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
        }
    }
}
