using System.Net;
using System.Net.Http.Json;
using Gatehouse.Agent.Storage;
using Gatehouse.Agent.Transport;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gatehouse.UnitTests.Agent;

public sealed class EventSendLoopTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gh-agent-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    [Fact]
    public async Task Failed_upload_keeps_events_for_retry()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new PendingEventStore(new AgentDatabase(_dir));
        var t0 = new DateTimeOffset(2026, 11, 10, 8, 0, 0, TimeSpan.Zero);
        for (var i = 0; i < 3; i++)
        {
            await store.AddAsync(PendingEventStoreTests.Event(t0.AddSeconds(i)), ct);
        }

        var fail = true;
        var bodies = new List<DoorEventsRequest>();
        var handler = new StubHandler(request =>
        {
            bodies.Add(request.Content!.ReadFromJsonAsync<DoorEventsRequest>(ConnectorJson.Options).GetAwaiter().GetResult()!);
            return fail
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.Accepted) { Content = JsonContent.Create(new DoorEventsResponse(3, 0), options: ConnectorJson.Options) };
        });
        var client = new GatewayClient(new HttpClient(handler) { BaseAddress = new Uri("https://gw/") }, "1.0");
        var loop = new EventSendLoop(client, store, TimeProvider.System, NullLogger<EventSendLoop>.Instance);

        Assert.Equal(PollCycleOutcome.Backoff, await loop.RunOnceAsync(ct));
        Assert.Equal(3, await store.CountAsync(ct));

        fail = false;
        Assert.Equal(PollCycleOutcome.Continue, await loop.RunOnceAsync(ct));
        Assert.Equal(0, await store.CountAsync(ct));
        Assert.All(handler.Paths, p => Assert.Equal("/connector/v1/events", p));
        Assert.Equal(bodies[0].Events.Select(e => e.EventId), bodies[1].Events.Select(e => e.EventId));
        Assert.Equal(3, bodies[1].Events.Count);
    }
}
