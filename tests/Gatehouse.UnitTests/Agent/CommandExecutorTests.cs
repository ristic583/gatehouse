using System.Text.Json;
using Gatehouse.Agent.Execution;
using Gatehouse.Agent.Storage;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Adapters;
using Gatehouse.Connector.Contracts.Events;
using Gatehouse.Connector.Contracts.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gatehouse.UnitTests.Agent;

public sealed class CommandExecutorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gh-agent-" + Guid.NewGuid().ToString("N"));
    private readonly FakePacsAdapter _adapter = new();
    private readonly CommandExecutor _executor;

    public CommandExecutorTests()
    {
        _executor = new CommandExecutor(_adapter, new ProcessedCommandStore(new AgentDatabase(_dir)), new NullSink(), TimeProvider.System, NullLogger<CommandExecutor>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    private static CommandEnvelope Envelope(string type, string key, Guid? id = null) => new(
        id ?? Guid.NewGuid(), key, type, 1, "t", Guid.NewGuid(), "person:1", 1, "Normal", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(1),
        JsonSerializer.SerializeToElement(new { personRef = "p", cardNumber = "1234567890", vendorAccessLevelRef = "AL", validFrom = DateTimeOffset.UnixEpoch, validTo = DateTimeOffset.UnixEpoch.AddDays(1) }, ConnectorJson.Options));

    [Fact]
    public async Task Same_key_twice_calls_adapter_once()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = await _executor.ExecuteAsync(Envelope("GrantAccess", "k1"), ct);
        var second = await _executor.ExecuteAsync(Envelope("GrantAccess", "k1"), ct);
        Assert.Equal(1, _adapter.GrantCalls);
        Assert.Equal(AckStatus.Applied, second.Status);
        Assert.Equal(first.PacsRef, second.PacsRef);
    }

    [Fact]
    public async Task Retryable_failure_is_not_stored()
    {
        var ct = TestContext.Current.CancellationToken;
        _adapter.GrantResult = AdapterResult.Failed("x.transient", "down", true);
        var ack = await _executor.ExecuteAsync(Envelope("GrantAccess", "k2"), ct);
        Assert.Equal(AckStatus.Failed, ack.Status);
        await _executor.ExecuteAsync(Envelope("GrantAccess", "k2"), ct);
        Assert.Equal(2, _adapter.GrantCalls);
    }

    [Fact]
    public async Task Adapter_exception_becomes_retryable_failure()
    {
        _adapter.ThrowOnGrant = true;
        var ack = await _executor.ExecuteAsync(Envelope("GrantAccess", "k3"), TestContext.Current.CancellationToken);
        Assert.Equal(AckStatus.Failed, ack.Status);
        Assert.Equal("adapter.unhandled_exception", ack.Error!.Code);
        Assert.True(ack.Error.Retryable);
    }

    [Fact]
    public async Task Unknown_type_is_unsupported()
    {
        var ack = await _executor.ExecuteAsync(Envelope("Nope", "k4"), TestContext.Current.CancellationToken);
        Assert.Equal(AckStatus.Unsupported, ack.Status);
    }

    private sealed class NullSink : IRecordExportSink
    {
        public Task SendAsync(RecordExport chunk, CancellationToken ct) => Task.CompletedTask;
    }
}
