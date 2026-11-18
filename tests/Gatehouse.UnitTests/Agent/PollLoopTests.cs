using System.Net;
using System.Net.Http.Json;
using Gatehouse.Agent.Execution;
using Gatehouse.Agent.Storage;
using Gatehouse.Agent.Transport;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Events;
using Gatehouse.Connector.Contracts.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gatehouse.UnitTests.Agent;

public sealed class PollLoopTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gh-agent-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    private (PollLoop Loop, PendingAckStore Pending) Create(StubHandler handler, FakePacsAdapter? adapter = null)
    {
        var db = new AgentDatabase(_dir);
        var pending = new PendingAckStore(db);
        var client = new GatewayClient(new HttpClient(handler) { BaseAddress = new Uri("https://gw/") }, "1.0");
        var executor = new CommandExecutor(adapter ?? new FakePacsAdapter(), new ProcessedCommandStore(db), new NoSink(), TimeProvider.System, NullLogger<CommandExecutor>.Instance);
        return (new PollLoop(client, executor, pending, TimeProvider.System, NullLogger<PollLoop>.Instance), pending);
    }

    private static CommandAcked Ack(Guid id) => new(id, "k", AckStatus.Applied, "ref", null, null);

    [Fact]
    public async Task Pending_acks_are_flushed_before_next_poll()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        var handler = new StubHandler(r => r.RequestUri!.AbsolutePath.EndsWith("acks", StringComparison.Ordinal)
            ? Json(new AckResponse([new AckItemResult(id, true, null)]))
            : Json(new PollResponse(DateTimeOffset.UtcNow, [])));
        var (loop, pending) = Create(handler);
        await pending.AddAsync(Ack(id), DateTimeOffset.UnixEpoch, ct);
        Assert.Equal(PollCycleOutcome.Continue, await loop.RunOnceAsync(ct));
        Assert.EndsWith("acks", handler.Paths[0], StringComparison.Ordinal);
        Assert.EndsWith("poll", handler.Paths[1], StringComparison.Ordinal);
        Assert.Empty(await pending.ListAsync(10, ct));
    }

    [Fact]
    public async Task Ack_failure_keeps_ack_buffered()
    {
        var ct = TestContext.Current.CancellationToken;
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var (loop, pending) = Create(handler);
        await pending.AddAsync(Ack(Guid.NewGuid()), DateTimeOffset.UnixEpoch, ct);
        Assert.Equal(PollCycleOutcome.Backoff, await loop.RunOnceAsync(ct));
        Assert.Single(await pending.ListAsync(10, ct));
    }

    [Fact]
    public async Task Door_commands_in_different_partitions_run_in_parallel()
    {
        var ct = TestContext.Current.CancellationToken;
        var commands = Enumerable.Range(0, 8).Select(_ => Door(Guid.NewGuid())).ToList();
        var handler = new StubHandler(r => r.RequestUri!.AbsolutePath.EndsWith("acks", StringComparison.Ordinal)
            ? Json(new AckResponse(commands.Select(c => new AckItemResult(c.CommandId, true, null)).ToList()))
            : Json(new PollResponse(DateTimeOffset.UtcNow, commands)));
        var (loop, pending) = Create(handler, new FakePacsAdapter { DoorDelay = TimeSpan.FromMilliseconds(400) });

        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.Equal(PollCycleOutcome.Continue, await loop.RunOnceAsync(ct));

        Assert.True(watch.Elapsed < TimeSpan.FromMilliseconds(400 * 4), $"took {watch.Elapsed}");
        Assert.Empty(await pending.ListAsync(10, ct));
    }

    private static CommandEnvelope Door(Guid doorId)
    {
        var id = Guid.NewGuid();
        var payload = System.Text.Json.JsonSerializer.SerializeToElement(
            new Gatehouse.Connector.Contracts.Commands.Lockdown(["door-" + doorId.ToString("N")], Gatehouse.Connector.Contracts.Adapters.LockdownAction.Start, "drill"),
            ConnectorJson.Options);
        var now = DateTimeOffset.UtcNow;
        return new CommandEnvelope(id, $"Lockdown:{id}:{doorId}", ConnectorCommandTypes.Lockdown, 1, "t", Guid.NewGuid(), $"door:{doorId}", 1, "High", now, now.AddMinutes(5), payload);
    }

    private static HttpResponseMessage Json<T>(T body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body, options: ConnectorJson.Options) };

    private sealed class NoSink : IRecordExportSink
    {
        public Task SendAsync(RecordExport chunk, CancellationToken ct) => Task.CompletedTask;
    }
}
