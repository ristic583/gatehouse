using System.Text.Json;
using Gatehouse.Agent.Execution;
using Gatehouse.Agent.Storage;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Events;
using Gatehouse.Connector.Contracts.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Gatehouse.UnitTests.Agent;

/// <summary>GH-CONN-01 (grants outside their window) and GH-LIVE-01 (late door commands) on the agent.</summary>
public sealed class CommandExecutorValidityTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly string[] DoorRefs = ["D-1"];
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gh-agent-" + Guid.NewGuid().ToString("N"));
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
    public async Task Grant_with_future_window_is_not_applied_without_native_validity()
    {
        var adapter = new FakePacsAdapter { Capabilities = new("Cloud", "1", false, true, true, true, true, [], 100) };

        var ack = await Executor(adapter).ExecuteAsync(Grant(Now.AddDays(1), Now.AddDays(2)), Ct);

        Assert.Equal(AckStatus.Failed, ack.Status);
        Assert.Equal(CommandExecutor.GrantNotYetValidCode, ack.Error!.Code);
        Assert.True(ack.Error.Retryable);
        Assert.Equal(0, adapter.GrantCalls);
    }

    [Fact]
    public async Task Grant_with_ended_window_is_rejected_without_native_validity()
    {
        var adapter = new FakePacsAdapter { Capabilities = new("Cloud", "1", false, true, true, true, true, [], 100) };

        var ack = await Executor(adapter).ExecuteAsync(Grant(Now.AddDays(-2), Now.AddDays(-1)), Ct);

        Assert.Equal(AckStatus.Failed, ack.Status);
        Assert.Equal(CommandExecutor.GrantWindowEndedCode, ack.Error!.Code);
        Assert.False(ack.Error.Retryable);
        Assert.Equal(0, adapter.GrantCalls);
    }

    [Fact]
    public async Task Grant_inside_window_or_with_native_validity_is_applied()
    {
        var cloud = new FakePacsAdapter { Capabilities = new("Cloud", "1", false, true, true, true, true, [], 100) };
        var native = new FakePacsAdapter();

        var inside = await Executor(cloud).ExecuteAsync(Grant(Now.AddMinutes(1), Now.AddDays(1)), Ct);
        var future = await Executor(native).ExecuteAsync(Grant(Now.AddDays(1), Now.AddDays(2), "g2"), Ct);

        Assert.Equal(AckStatus.Applied, inside.Status);
        Assert.Equal(AckStatus.Applied, future.Status);
        Assert.Equal(1, cloud.GrantCalls);
        Assert.Equal(1, native.GrantCalls);
    }

    [Fact]
    public async Task Momentary_unlock_after_deadline_is_not_executed()
    {
        var adapter = new FakePacsAdapter();
        var executor = Executor(adapter);

        var late = await executor.ExecuteAsync(Unlock(Now.AddSeconds(-1), "u1"), Ct);
        var onTime = await executor.ExecuteAsync(Unlock(Now.AddSeconds(30), "u2"), Ct);

        Assert.Equal(AckStatus.Failed, late.Status);
        Assert.Equal(CommandExecutor.CommandExpiredCode, late.Error!.Code);
        Assert.Equal(AckStatus.Applied, onTime.Status);
        Assert.Equal(1, adapter.DoorCalls);
    }

    [Fact]
    public async Task Momentary_unlock_without_deadline_uses_issue_time_plus_default()
    {
        var adapter = new FakePacsAdapter();
        var executor = Executor(adapter);
        var payload = new { pacsDoorRefs = DoorRefs, durationSeconds = 5, reason = "Delivery at the dock" };

        var old = await executor.ExecuteAsync(Envelope("MomentaryUnlock", "u1", payload) with { IssuedAt = Now.AddMinutes(-10) }, Ct);
        var fresh = await executor.ExecuteAsync(Envelope("MomentaryUnlock", "u2", payload), Ct);

        Assert.Equal((AckStatus.Failed, CommandExecutor.CommandExpiredCode), (old.Status, old.Error!.Code));
        Assert.Equal(AckStatus.Applied, fresh.Status);
        Assert.Equal(1, adapter.DoorCalls);
    }

    private CommandExecutor Executor(FakePacsAdapter adapter) =>
        new(adapter, new ProcessedCommandStore(new AgentDatabase(Path.Combine(_dir, Guid.NewGuid().ToString("N")))), new NullSink(), _time,
            NullLogger<CommandExecutor>.Instance, schedules: null);

    private static CommandEnvelope Grant(DateTimeOffset from, DateTimeOffset to, string key = "g1") => Envelope("GrantAccess", key,
        new { personRef = "p1", cardNumber = "1234567890", vendorAccessLevelRef = "AG-1", validFrom = from, validTo = to });

    private static CommandEnvelope Unlock(DateTimeOffset executeBy, string key) => Envelope("MomentaryUnlock", key,
        new { pacsDoorRefs = DoorRefs, durationSeconds = 5, reason = "Delivery at the dock", executeBy });

    private static CommandEnvelope Envelope(string type, string key, object payload) => new(
        Guid.NewGuid(), key, type, 1, "t", Guid.NewGuid(), "person:1", 1, "Normal", Now, Now.AddMinutes(2),
        JsonSerializer.SerializeToElement(payload, ConnectorJson.Options));

    private sealed class NullSink : IRecordExportSink
    {
        public Task SendAsync(RecordExport chunk, CancellationToken ct) => Task.CompletedTask;
    }
}
