using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Building.Simulator;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Gatehouse.IntegrationTests.Simulators;

public sealed class BuildingSimulatorTests : IDisposable
{
    private readonly FakeController _controller = new();
    private readonly FakeCloudPacs _cloud = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero));
    private readonly DoorSequencer _sequencer;
    private readonly BuildingSimulator _simulator;
    private readonly DoorControllerRouter _router;

    public BuildingSimulatorTests()
    {
        var client = new LegacyPacsControllerClient(new HttpClient(_controller) { BaseAddress = new Uri("http://legacypacs") });
        var cloud = new CloudPacsControllerClient(new HttpClient(_cloud) { BaseAddress = new Uri("http://cloudpacs") });
        _router = new DoorControllerRouter(client, cloud, _time, NullLogger<DoorControllerRouter>.Instance);
        _sequencer = new DoorSequencer(_router, _time, NullLogger<DoorSequencer>.Instance);
        _simulator = new BuildingSimulator(_router, _sequencer);
    }

    public void Dispose()
    {
        _controller.Dispose();
        _cloud.Dispose();
    }

    [Fact]
    public async Task Swipe_at_cloudpacs_door_goes_to_cloudpacs()
    {
        var decision = await _simulator.SwipeAsync(new SwipeCommand("D-AAR-DOCK", CardNumber: "200", PersonRef: "AG-P1"), TestContext.Current.CancellationToken);
        Assert.Equal("Granted", decision!.Decision);
        Assert.Equal(42, decision.EventId);
        Assert.Equal(["D-AAR-DOCK:200:AG-P1"], _cloud.Swipes);
        _time.Advance(TimeSpan.FromSeconds(5));
        await _sequencer.WhenIdleAsync();
        Assert.Equal(["D-AAR-DOCK:door.opened", "D-AAR-DOCK:door.closed", "D-AAR-DOCK:door.relocked"], _cloud.DoorEvents);
        Assert.Empty(_controller.DoorEvents);

        Assert.True(await _simulator.RunScenarioAsync("forced-door", "D-AAR-OFFICE", TestContext.Current.CancellationToken));
        _time.Advance(TimeSpan.FromSeconds(10));
        await _sequencer.WhenIdleAsync();
        Assert.Equal(["D-AAR-OFFICE:door.forced", "D-AAR-OFFICE:door.closed"], _cloud.DoorEvents.Skip(3));
        Assert.Empty(_controller.DoorEvents);
    }

    [Fact]
    public async Task Doors_lists_both_vendors()
    {
        var doors = await _router.ListDoorsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["D-CPH-LAB", "D-AAR-DOCK", "D-AAR-OFFICE"], doors.Select(d => d.DoorRef));
        Assert.Equal("AG-AAR-DOCK", doors[1].AccessLevelRef);
    }

    [Fact]
    public async Task Granted_swipe_runs_door_cycle()
    {
        var decision = await _simulator.SwipeAsync(new SwipeCommand("D-CPH-LAB", CardNumber: "100"), TestContext.Current.CancellationToken);
        Assert.Equal("Granted", decision!.Decision);
        Assert.Empty(_controller.DoorEvents);
        _time.Advance(TimeSpan.FromSeconds(5));
        await _sequencer.WhenIdleAsync();
        Assert.Equal(["D-CPH-LAB:DoorOpened", "D-CPH-LAB:DoorClosed", "D-CPH-LAB:DoorRelocked"], _controller.DoorEvents);
    }

    [Fact]
    public async Task Denied_swipe_does_not_open_door()
    {
        var decision = await _simulator.SwipeAsync(new SwipeCommand("D-CPH-LAB", CardNumber: "999"), TestContext.Current.CancellationToken);
        Assert.Equal("Denied", decision!.Decision);
        _time.Advance(TimeSpan.FromSeconds(10));
        await _sequencer.WhenIdleAsync();
        Assert.Empty(_controller.DoorEvents);
    }

    [Fact]
    public async Task Forced_door_scenario_writes_forced_then_closed()
    {
        Assert.True(await _simulator.RunScenarioAsync("forced-door", "D-CPH-VAULT", TestContext.Current.CancellationToken));
        _time.Advance(TimeSpan.FromSeconds(9));
        await WaitForAsync(1);
        Assert.Equal(["D-CPH-VAULT:DoorForced"], _controller.DoorEvents);
        _time.Advance(TimeSpan.FromSeconds(1));
        await _sequencer.WhenIdleAsync();
        Assert.Equal(["D-CPH-VAULT:DoorForced", "D-CPH-VAULT:DoorClosed"], _controller.DoorEvents);
        Assert.False(await _simulator.RunScenarioAsync("nope", null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Held_open_scenario_writes_held_open()
    {
        Assert.True(await _simulator.RunScenarioAsync("held-open", null, TestContext.Current.CancellationToken));
        _time.Advance(TimeSpan.FromSeconds(2));
        await WaitForAsync(3);
        Assert.Equal(["D-CPH-LOBBY:DoorUnlocked", "D-CPH-LOBBY:DoorOpened", "D-CPH-LOBBY:DoorHeldOpen"], _controller.DoorEvents);
        _time.Advance(TimeSpan.FromSeconds(18));
        await _sequencer.WhenIdleAsync();
        Assert.Equal("D-CPH-LOBBY:DoorClosed", _controller.DoorEvents[^1]);
    }

    private async Task WaitForAsync(int count)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        while (_controller.DoorEvents.Count < count)
        {
            await Task.Delay(10, cts.Token);
        }

        await Task.Delay(50, cts.Token);
    }

    private sealed class FakeController : HttpMessageHandler
    {
        private readonly ConcurrentQueue<string> _doorEvents = new();

        public IReadOnlyList<string> DoorEvents => [.. _doorEvents];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            using var body = request.Content is null ? null : JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
            switch (path)
            {
                case "/controller/swipe":
                    var granted = body!.RootElement.GetProperty("cardNumber").GetString() == "100";
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { decision = granted ? "Granted" : "Denied", eventId = 1 }) };
                case "/doors":
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { new { doorRef = "D-CPH-LAB", accessLevelRef = "AL-CPH-LAB", lockdown = false } }) };
                case "/controller/door-events":
                    _doorEvents.Enqueue(body!.RootElement.GetProperty("doorRef").GetString() + ":" + body.RootElement.GetProperty("type").GetString());
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { eventId = 2 }) };
                default:
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        }
    }

    private sealed class FakeCloudPacs : HttpMessageHandler
    {
        private readonly ConcurrentQueue<string> _doorEvents = new();
        private readonly ConcurrentQueue<string> _swipes = new();

        public IReadOnlyList<string> DoorEvents => [.. _doorEvents];

        public IReadOnlyList<string> Swipes => [.. _swipes];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            using var body = request.Content is null ? null : JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
            if (path == "/dev/doors")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new[] { new { doorRef = "D-AAR-DOCK", groupId = "AG-AAR-DOCK" }, new { doorRef = "D-AAR-OFFICE", groupId = "AG-AAR-OFFICE" } }),
                };
            }

            if (path == "/dev/swipes")
            {
                var root = body!.RootElement;
                _swipes.Enqueue(root.GetProperty("doorRef").GetString() + ":" + root.GetProperty("cardNumber").GetString() + ":" + root.GetProperty("externalRef").GetString());
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { decision = "granted", eventId = 42 }) };
            }

            if (path.StartsWith("/dev/doors/", StringComparison.Ordinal) && path.EndsWith("/events", StringComparison.Ordinal))
            {
                _doorEvents.Enqueue(path["/dev/doors/".Length..^"/events".Length] + ":" + body!.RootElement.GetProperty("type").GetString());
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { eventId = 43 }) };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }
}
