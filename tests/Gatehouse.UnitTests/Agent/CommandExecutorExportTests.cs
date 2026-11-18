using System.Text.Json;
using Gatehouse.Agent.Execution;
using Gatehouse.Agent.Storage;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Adapters;
using Gatehouse.Connector.Contracts.Events;
using Gatehouse.Connector.Contracts.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gatehouse.UnitTests.Agent;

public sealed class CommandExecutorExportTests : IDisposable
{
    private static readonly string[] RecordTypes = ["AccessAssignment"];
    private static readonly string[] Levels = ["AL"];

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gh-agent-" + Guid.NewGuid().ToString("N"));
    private readonly FakePacsAdapter _adapter = new();
    private readonly ScriptedSink _sink = new();
    private readonly CommandExecutor _executor;

    public CommandExecutorExportTests()
    {
        _executor = new CommandExecutor(_adapter, new ProcessedCommandStore(new AgentDatabase(_dir)), _sink, TimeProvider.System, NullLogger<CommandExecutor>.Instance);
        for (var i = 0; i < 1500; i++)
        {
            _adapter.ExportedRecords.Add(new PacsRecord(RecordType.AccessAssignment, $"a-{i}", "p", "100200300", "AL", null, null, true));
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    private static CommandEnvelope Export(Guid exportId) => new(
        Guid.NewGuid(), $"ExportRecords:{exportId}:1", ConnectorCommandTypes.ExportRecords, 1, "t", Guid.NewGuid(), $"export:{exportId}", 1, "Normal",
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(1),
        JsonSerializer.SerializeToElement(new { exportId, recordTypes = RecordTypes, vendorAccessLevelRefs = Levels }, ConnectorJson.Options));

    [Fact]
    public async Task Export_restarts_once_on_out_of_order()
    {
        var ct = TestContext.Current.CancellationToken;
        _sink.FailOnce = (1, new ExportRestartRequiredException());

        var ack = await _executor.ExecuteAsync(Export(Guid.NewGuid()), ct);

        Assert.Equal(AckStatus.Applied, ack.Status);
        Assert.Equal([0, 1, 0, 1], _sink.Indexes);
        Assert.Equal(CommandExecutor.ExportChunkSize, _sink.Chunks[0].Records.Length);
        Assert.True(_sink.Chunks[^1].IsLast);
        Assert.Equal(500, _sink.Chunks[^1].Records.Length);
        Assert.Single(_sink.Chunks.Select(c => c.CapturedAt).Skip(2).Distinct());
    }

    [Fact]
    public async Task Export_rejected_acks_failed_non_retryable()
    {
        var ct = TestContext.Current.CancellationToken;
        _sink.FailOnce = (0, new ExportRejectedException("connector.export_closed"));

        var ack = await _executor.ExecuteAsync(Export(Guid.NewGuid()), ct);

        Assert.Equal(AckStatus.Failed, ack.Status);
        Assert.NotNull(ack.Error);
        Assert.Equal("connector.export_closed", ack.Error.Code);
        Assert.False(ack.Error.Retryable);
        Assert.Equal([0], _sink.Indexes);
    }

    private sealed class ScriptedSink : IRecordExportSink
    {
        public (int Index, Exception Error)? FailOnce { get; set; }

        public List<RecordExport> Chunks { get; } = [];

        public List<int> Indexes { get; } = [];

        public Task SendAsync(RecordExport chunk, CancellationToken ct)
        {
            Indexes.Add(chunk.ChunkIndex);
            if (FailOnce is { } f && f.Index == chunk.ChunkIndex)
            {
                FailOnce = null;
                throw f.Error;
            }

            Chunks.Add(chunk);
            return Task.CompletedTask;
        }
    }
}
