using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CloudPacs.Simulator;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Simulators;

public sealed class CloudPacsSimulatorTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _api = null!;
    private HttpClient _dev = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _app = CloudPacsSimulatorProgram.CreateApp(["--urls=http://127.0.0.1:0"]);
        await _app.StartAsync();
        var address = new Uri(_app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());
        _dev = new HttpClient { BaseAddress = address };
        _api = new HttpClient { BaseAddress = address };
        _api.DefaultRequestHeaders.Add("X-Api-Key", CloudPacsOptions.DefaultApiKey);
    }

    public async ValueTask DisposeAsync()
    {
        _api.Dispose();
        _dev.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private async Task UpsertAsync(string externalRef, string card) =>
        (await _api.PutAsJsonAsync($"/api/cardholders/{externalRef}", new { givenName = "Test", familyName = "Person", cardNumber = card }, Ct)).EnsureSuccessStatusCode();

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

    [Fact]
    public async Task Api_requires_api_key_401()
    {
        var response = await _dev.GetAsync("/api/cardholders", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Membership_put_twice_returns_same_assignment()
    {
        await UpsertAsync("p1", "9001");
        var first = await JsonAsync(await _api.PutAsJsonAsync("/api/access-groups/AG-AAR-DOCK/members/p1", new { cardNumber = "9001" }, Ct));
        var second = await JsonAsync(await _api.PutAsJsonAsync("/api/access-groups/AG-AAR-DOCK/members/p1", new { cardNumber = "9001" }, Ct));
        Assert.Equal(first.GetProperty("assignmentId").GetGuid(), second.GetProperty("assignmentId").GetGuid());
        var page = await JsonAsync(await _api.GetAsync("/api/assignments", Ct));
        Assert.Single(page.GetProperty("items").EnumerateArray(), a => a.GetProperty("externalRef").GetString() == "p1");
    }

    [Fact]
    public async Task Unknown_group_is_404_before_unknown_cardholder()
    {
        var response = await _api.PutAsJsonAsync("/api/access-groups/AG-NOPE/members/nobody", new { cardNumber = "1" }, Ct);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("unknown_access_group", (await JsonAsync(response)).GetProperty("error").GetString());
        response = await _api.PutAsJsonAsync("/api/access-groups/AG-AAR-DOCK/members/nobody", new { cardNumber = "1" }, Ct);
        Assert.Equal("unknown_cardholder", (await JsonAsync(response)).GetProperty("error").GetString());
    }

    [Fact]
    public async Task Delete_membership_keeps_inactive_assignment()
    {
        await UpsertAsync("p2", "9002");
        (await _api.PutAsJsonAsync("/api/access-groups/AG-AAR-DOCK/members/p2", new { cardNumber = "9002" }, Ct)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NoContent, (await _api.DeleteAsync("/api/access-groups/AG-AAR-DOCK/members/p2", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _api.DeleteAsync("/api/access-groups/AG-AAR-DOCK/members/p2", Ct)).StatusCode);
        var page = await JsonAsync(await _api.GetAsync("/api/assignments", Ct));
        var row = Assert.Single(page.GetProperty("items").EnumerateArray(), a => a.GetProperty("externalRef").GetString() == "p2");
        Assert.False(row.GetProperty("active").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, row.GetProperty("removedAt").ValueKind);
    }

    [Fact]
    public async Task Fail_next_returns_503_then_recovers()
    {
        (await _dev.PostAsJsonAsync("/dev/chaos", new { failNext = 1 }, Ct)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await _api.GetAsync("/api/access-groups", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _api.GetAsync("/api/access-groups", Ct)).StatusCode);
    }

    [Fact]
    public async Task Rate_limit_next_returns_429_with_retry_after()
    {
        (await _dev.PostAsJsonAsync("/dev/chaos", new { rateLimitNext = 1 }, Ct)).EnsureSuccessStatusCode();
        var response = await _api.GetAsync("/api/cards", Ct);
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(1), response.Headers.RetryAfter?.Delta);
        Assert.Equal(HttpStatusCode.OK, (await _api.GetAsync("/api/cards", Ct)).StatusCode);
    }

    [Fact]
    public async Task Pin_is_accepted_and_not_returned()
    {
        Assert.Equal(HttpStatusCode.NoContent, (await _api.PutAsJsonAsync("/api/cardholders/p3/pin", new { pin = "4711" }, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _api.PutAsJsonAsync("/api/cardholders/p3/pin", new { pin = "12" }, Ct)).StatusCode);
        Assert.True(_app.Services.GetRequiredService<CloudPacsStore>().HasPin("p3"));
        await UpsertAsync("p3", "9003");
        var holders = await _api.GetStringAsync("/api/cardholders", Ct);
        Assert.DoesNotContain("4711", holders, StringComparison.Ordinal);
    }

    private static Uri Address(WebApplication app) =>
        new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());

    private static async Task<(WebApplication App, ConcurrentQueue<(string Signature, byte[] Body)> Calls, Uri Url)> ReceiverAsync(int failFirst = 0)
    {
        var calls = new ConcurrentQueue<(string, byte[])>();
        var remainingFailures = failFirst;
        var builder = WebApplication.CreateSlimBuilder(["--urls=http://127.0.0.1:0"]);
        var app = builder.Build();
        app.MapPost("/hook", async (HttpRequest request) =>
        {
            using var buffer = new MemoryStream();
            await request.Body.CopyToAsync(buffer);
            if (Interlocked.Decrement(ref remainingFailures) >= 0)
            {
                return Results.StatusCode(500);
            }

            calls.Enqueue((request.Headers["X-CloudPacs-Signature"].ToString(), buffer.ToArray()));
            return Results.NoContent();
        });
        await app.StartAsync(Ct);
        return (app, calls, new Uri(Address(app), "/hook"));
    }

    private async Task RegisterAsync(Uri url, string secret = "s3cret") =>
        (await _api.PostAsJsonAsync("/api/webhooks", new { url = url.ToString(), secret }, Ct)).EnsureSuccessStatusCode();

    private static async Task<List<JsonElement>> WaitForEventsAsync(ConcurrentQueue<(string Signature, byte[] Body)> calls, Func<JsonElement, bool> match, int count = 1, int seconds = 10)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < deadline)
        {
            var found = calls.SelectMany(c => JsonDocument.Parse(c.Body).RootElement.EnumerateArray()).Where(match).ToList();
            if (found.Count >= count)
            {
                return found;
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        return [];
    }

    [Fact]
    public async Task Webhook_receives_signed_event()
    {
        var (receiver, calls, url) = await ReceiverAsync();
        await using var _ = receiver;
        await RegisterAsync(url);
        (await _dev.PostAsJsonAsync("/dev/doors/D-AAR-DOCK/events", new { type = "door.forced", cardNumber = "777" }, Ct)).EnsureSuccessStatusCode();
        var events = await WaitForEventsAsync(calls, e => e.GetProperty("cardNumber").GetString() == "777");
        var e = Assert.Single(events);
        Assert.Equal("door.forced", e.GetProperty("type").GetString());
        Assert.Equal("D-AAR-DOCK", e.GetProperty("doorRef").GetString());
        var (signature, body) = calls.First(c => JsonDocument.Parse(c.Body).RootElement.EnumerateArray().Any(x => x.GetProperty("cardNumber").GetString() == "777"));
        Assert.Equal(WebhookDispatcher.Sign("s3cret", body), signature);
    }

    [Fact]
    public async Task Events_before_subscription_are_delivered_after_registration()
    {
        (await _dev.PostAsJsonAsync("/dev/doors/D-AAR-OFFICE/events", new { type = "door.opened", cardNumber = "778" }, Ct)).EnsureSuccessStatusCode();
        await Task.Delay(400, Ct);
        var (receiver, calls, url) = await ReceiverAsync();
        await using var _ = receiver;
        await RegisterAsync(url);
        Assert.Single(await WaitForEventsAsync(calls, e => e.GetProperty("cardNumber").GetString() == "778"));
    }

    [Fact]
    public async Task Failed_delivery_is_retried()
    {
        var (receiver, calls, url) = await ReceiverAsync(failFirst: 1);
        await using var _ = receiver;
        await RegisterAsync(url);
        (await _dev.PostAsJsonAsync("/dev/doors/D-AAR-DOCK/events", new { type = "door.closed", cardNumber = "779" }, Ct)).EnsureSuccessStatusCode();
        Assert.Single(await WaitForEventsAsync(calls, e => e.GetProperty("cardNumber").GetString() == "779"));
    }

    [Fact]
    public async Task Swipe_with_active_membership_is_granted()
    {
        await UpsertAsync("p4", "9004");
        (await _api.PutAsJsonAsync("/api/access-groups/AG-AAR-DOCK/members/p4", new { cardNumber = "9004" }, Ct)).EnsureSuccessStatusCode();
        var result = await JsonAsync(await _dev.PostAsJsonAsync("/dev/swipes", new { doorRef = "D-AAR-DOCK", cardNumber = "9004" }, Ct));
        Assert.Equal("granted", result.GetProperty("decision").GetString());
        Assert.True(result.GetProperty("eventId").GetInt64() > 0);
    }

    [Fact]
    public async Task Swipe_without_membership_is_denied()
    {
        await UpsertAsync("p5", "9005");
        var result = await JsonAsync(await _dev.PostAsJsonAsync("/dev/swipes", new { doorRef = "D-AAR-OFFICE", cardNumber = "9005" }, Ct));
        Assert.Equal("denied", result.GetProperty("decision").GetString());
    }

    [Fact]
    public async Task Unlock_emits_unlocked_then_relocked()
    {
        var (receiver, calls, url) = await ReceiverAsync();
        await using var _ = receiver;
        await RegisterAsync(url);
        Assert.Equal(HttpStatusCode.Accepted, (await _api.PostAsJsonAsync("/api/doors/D-AAR-OFFICE/unlock", new { seconds = 1 }, Ct)).StatusCode);
        var events = await WaitForEventsAsync(calls, e => e.GetProperty("doorRef").GetString() == "D-AAR-OFFICE", count: 2);
        Assert.Equal(["door.unlocked", "door.relocked"], events.Select(e => e.GetProperty("type").GetString()).ToArray());
    }
}
