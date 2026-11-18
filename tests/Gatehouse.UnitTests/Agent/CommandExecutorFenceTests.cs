using System.Text.Json;
using Gatehouse.Agent.Execution;
using Gatehouse.Agent.Storage;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Events;
using Gatehouse.Connector.Contracts.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Gatehouse.UnitTests.Agent;

/// <summary>GH-CONN-02: a grant overtaken by a High priority revoke of the same person and level is never executed.</summary>
public sealed class CommandExecutorFenceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 8, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gh-agent-" + Guid.NewGuid().ToString("N"));
    private readonly FakePacsAdapter _adapter = new();
    private readonly FakeTimeProvider _time = new(Now);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    [Fact]
    public async Task Grant_after_high_priority_revoke_is_fenced()
    {
        var executor = NewExecutor();

        Assert.Equal(AckStatus.Applied, (await executor.ExecuteAsync(Envelope("RevokeAccess", 8, "High"), Ct)).Status);
        var ack = await executor.ExecuteAsync(Envelope("GrantAccess", 7, "Normal"), Ct);

        Assert.Equal(AckStatus.Failed, ack.Status);
        Assert.Equal(CommandExecutor.GrantFencedCode, ack.Error!.Code);
        Assert.False(ack.Error.Retryable);
        Assert.Equal(0, _adapter.GrantCalls);
    }

    [Fact]
    public async Task Grant_with_higher_sequence_is_not_fenced()
    {
        var executor = NewExecutor();
        await executor.ExecuteAsync(Envelope("RevokeAccess", 8, "High"), Ct);
        await executor.ExecuteAsync(Envelope("RevokeAccess", 20, "Normal", level: "AL-OTHER"), Ct);

        Assert.Equal(AckStatus.Applied, (await executor.ExecuteAsync(Envelope("GrantAccess", 9, "Normal"), Ct)).Status);
        Assert.Equal(AckStatus.Applied, (await executor.ExecuteAsync(Envelope("GrantAccess", 3, "Normal", level: "AL-OTHER"), Ct)).Status);
        Assert.Equal(2, _adapter.GrantCalls);
    }

    [Fact]
    public async Task Fence_survives_restart()
    {
        await NewExecutor().ExecuteAsync(Envelope("RevokeAccess", 8, "High"), Ct);

        var restarted = NewExecutor();
        var fenced = await restarted.ExecuteAsync(Envelope("GrantAccess", 7, "Normal"), Ct);
        _time.Advance(FenceStore.Retention + TimeSpan.FromMinutes(1));
        var afterRetention = await restarted.ExecuteAsync(Envelope("GrantAccess", 6, "Normal"), Ct);

        Assert.Equal(CommandExecutor.GrantFencedCode, fenced.Error?.Code);
        Assert.Equal(AckStatus.Applied, afterRetention.Status);
    }

    /// <summary>A new executor on the same data directory, as after an agent restart.</summary>
    private CommandExecutor NewExecutor()
    {
        var db = new AgentDatabase(_dir);
        return new CommandExecutor(_adapter, new ProcessedCommandStore(db), new NullSink(), _time, NullLogger<CommandExecutor>.Instance,
            fences: new FenceStore(db));
    }

    private static CommandEnvelope Envelope(string type, long sequence, string priority, string level = "AL-SERVER") => new(
        Guid.NewGuid(), $"{type}:{Guid.NewGuid()}:1", type, 1, "t", Guid.NewGuid(), "person:p1", sequence, priority, Now, Now.AddMinutes(1),
        JsonSerializer.SerializeToElement(
            new { personRef = "p1", cardNumber = "1234567890", vendorAccessLevelRef = level, validFrom = Now.AddDays(-1), validTo = Now.AddDays(30) },
            ConnectorJson.Options));

    private sealed class NullSink : IRecordExportSink
    {
        public Task SendAsync(RecordExport chunk, CancellationToken ct) => Task.CompletedTask;
    }
}
