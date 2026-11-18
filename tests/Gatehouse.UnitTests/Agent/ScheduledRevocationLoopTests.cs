using System.Net;
using System.Net.Http.Json;
using Gatehouse.Agent;
using Gatehouse.Agent.Execution;
using Gatehouse.Agent.Storage;
using Gatehouse.Agent.Transport;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Adapters;
using Gatehouse.Connector.Contracts.Events;
using Gatehouse.Connector.Contracts.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Gatehouse.UnitTests.Agent;

public sealed class ScheduledRevocationLoopTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gh-agent-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(Start);
    private readonly FakePacsAdapter _adapter = new() { Capabilities = new("Cloud", "1", false, true, true, true, true, [], 100) };
    private readonly ScheduledRevocationStore _store;
    private readonly ScheduledRevocationLoop _loop;

    public ScheduledRevocationLoopTests()
    {
        _store = new ScheduledRevocationStore(new AgentDatabase(_dir));
        _loop = new ScheduledRevocationLoop(_adapter, _store, _time, NullLogger<ScheduledRevocationLoop>.Instance);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    [Fact]
    public async Task Due_row_is_revoked_and_marked_done()
    {
        await _store.ScheduleAsync("p1", "123456789", "AG-AAR-DOCK", Start.AddMinutes(5), Ct);

        Assert.Equal(0, await _loop.RunOnceAsync(Ct));
        Assert.Empty(_adapter.Revokes);

        _time.Advance(TimeSpan.FromMinutes(6));
        Assert.Equal(1, await _loop.RunOnceAsync(Ct));

        var revoke = Assert.Single(_adapter.Revokes);
        Assert.Equal(new AccessRevoke("p1", "123456789", "AG-AAR-DOCK"), revoke);
        Assert.Equal(0, await _store.PendingCountAsync(Ct));
    }

    [Fact]
    public async Task Retryable_failure_keeps_row()
    {
        await _store.ScheduleAsync("p1", "123456789", "AG-AAR-DOCK", Start, Ct);
        _adapter.RevokeResult = AdapterResult.Failed("cloudpacs.server_error", "down", true);

        Assert.Equal(0, await _loop.RunOnceAsync(Ct));
        Assert.Equal(1, await _store.PendingCountAsync(Ct));

        _adapter.RevokeResult = AdapterResult.Applied(null);
        Assert.Equal(1, await _loop.RunOnceAsync(Ct));
        Assert.Equal(0, await _store.PendingCountAsync(Ct));
        Assert.Equal(2, _adapter.Revokes.Count);
    }

    [Fact]
    public async Task Permanent_failure_is_not_abandoned_and_retried_later()
    {
        await _store.ScheduleAsync("p1", "123456789", "AG-AAR-DOCK", Start, Ct);
        _adapter.RevokeResult = AdapterResult.Failed("cloudpacs.forbidden", "refused", false);

        Assert.Equal(0, await _loop.RunOnceAsync(Ct));
        Assert.Equal(1, await _store.PendingCountAsync(Ct));
        Assert.Equal(0, await _loop.RunOnceAsync(Ct));
        Assert.Single(_adapter.Revokes);

        _time.Advance(ScheduledRevocationLoop.PermanentFailureDelay);
        _adapter.RevokeResult = AdapterResult.Applied(null);
        Assert.Equal(1, await _loop.RunOnceAsync(Ct));
        Assert.Equal(2, _adapter.Revokes.Count);
        Assert.Equal(0, await _store.PendingCountAsync(Ct));
    }

    [Fact]
    public async Task Native_validity_adapter_does_nothing()
    {
        var native = new FakePacsAdapter();
        var loop = new ScheduledRevocationLoop(native, _store, _time, NullLogger<ScheduledRevocationLoop>.Instance);
        await _store.ScheduleAsync("p1", "123456789", "AG-AAR-DOCK", Start, Ct);

        Assert.Equal(0, await loop.RunOnceAsync(Ct));
        await loop.RunAsync(Ct);

        Assert.Empty(native.Revokes);
        Assert.Equal(1, await _store.PendingCountAsync(Ct));
    }

    [Fact]
    public async Task Permanent_failure_is_reported_in_heartbeat()
    {
        var bodies = new List<Heartbeat>();
        var handler = new StubHandler(request =>
        {
            bodies.Add(request.Content!.ReadFromJsonAsync<Heartbeat>(ConnectorJson.Options).GetAwaiter().GetResult()!);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new HeartbeatResponse(Start, 25, null), options: ConnectorJson.Options),
            };
        });
        var client = new GatewayClient(new HttpClient(handler) { BaseAddress = new Uri("https://gw/") }, "1.0");
        var db = new AgentDatabase(_dir);
        var heartbeat = new HeartbeatLoop(client, _adapter, new PendingAckStore(db), new PendingEventStore(db),
            new AgentOptions("https://gw/", _dir, null, "1.0", string.Empty), _time, NullLogger<HeartbeatLoop>.Instance, _store);
        await _store.ScheduleAsync("p1", "123456789", "AG-AAR-DOCK", Start, Ct);
        _adapter.RevokeResult = AdapterResult.Failed("cloudpacs.level_locked", "locked", false);

        await _loop.RunOnceAsync(Ct);
        _time.Advance(ScheduledRevocationLoop.PermanentFailureDelay);
        await _loop.RunOnceAsync(Ct);
        await heartbeat.SendOnceAsync(Ct);
        _adapter.RevokeResult = AdapterResult.Applied(null);
        _time.Advance(ScheduledRevocationLoop.PermanentFailureDelay);
        await _loop.RunOnceAsync(Ct);
        await heartbeat.SendOnceAsync(Ct);

        var reported = Assert.Single(bodies[0].RefusedRemovals!);
        Assert.Equal("p1:AG-AAR-DOCK", reported.RevocationRef);
        Assert.Equal("cloudpacs.level_locked", reported.Code);
        Assert.Equal(Start, reported.FirstRefusedAt);
        Assert.Equal(2, reported.Attempts);
        Assert.Empty(bodies[1].RefusedRemovals!);
    }
}
