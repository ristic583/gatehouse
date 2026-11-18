using System.Text.Json;
using Gatehouse.Agent.Execution;
using Gatehouse.Agent.Storage;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Events;
using Gatehouse.Connector.Contracts.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Gatehouse.UnitTests.Agent;

public sealed class CommandExecutorScheduledRevocationTests : IDisposable
{
    private static readonly DateTimeOffset ValidTo = new(2026, 11, 24, 8, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gh-agent-" + Guid.NewGuid().ToString("N"));
    private readonly FakePacsAdapter _adapter = new() { Capabilities = new("Cloud", "1", false, true, true, true, true, [], 100) };
    private readonly ScheduledRevocationStore _schedules;
    private readonly CommandExecutor _executor;

    public CommandExecutorScheduledRevocationTests()
    {
        var db = new AgentDatabase(_dir);
        _schedules = new ScheduledRevocationStore(db);
        _executor = new CommandExecutor(_adapter, new ProcessedCommandStore(db), new NullSink(), new FakeTimeProvider(new DateTimeOffset(2026, 11, 10, 8, 0, 0, TimeSpan.Zero)),
            NullLogger<CommandExecutor>.Instance, schedules: _schedules);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    private static CommandEnvelope Envelope(string type, string key) => new(
        Guid.NewGuid(), key, type, 1, "t", Guid.NewGuid(), "person:1", 1, "Normal", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddMinutes(1),
        JsonSerializer.SerializeToElement(new { personRef = "p1", cardNumber = "1234567890", vendorAccessLevelRef = "AG-AAR-DOCK", validFrom = DateTimeOffset.UnixEpoch, validTo = ValidTo }, ConnectorJson.Options));

    [Fact]
    public async Task Grant_without_native_validity_schedules_revocation()
    {
        var ack = await _executor.ExecuteAsync(Envelope("GrantAccess", "g1"), Ct);

        Assert.Equal(AckStatus.Applied, ack.Status);
        Assert.Empty(await _schedules.DueAsync(ValidTo.AddSeconds(-1), 10, Ct));
        var due = Assert.Single(await _schedules.DueAsync(ValidTo, 10, Ct));
        Assert.Equal("p1", due.PersonRef);
        Assert.Equal("AG-AAR-DOCK", due.VendorAccessLevelRef);
        Assert.Equal(ValidTo, due.RevokeAt);
    }

    [Fact]
    public async Task Revoke_cancels_schedule()
    {
        await _executor.ExecuteAsync(Envelope("GrantAccess", "g2"), Ct);
        Assert.Equal(1, await _schedules.PendingCountAsync(Ct));

        var ack = await _executor.ExecuteAsync(Envelope("RevokeAccess", "r2"), Ct);

        Assert.Equal(AckStatus.Applied, ack.Status);
        Assert.Equal(0, await _schedules.PendingCountAsync(Ct));

        await _executor.ExecuteAsync(Envelope("GrantAccess", "g2"), Ct);
        Assert.Equal(0, await _schedules.PendingCountAsync(Ct));
    }

    private sealed class NullSink : IRecordExportSink
    {
        public Task SendAsync(RecordExport chunk, CancellationToken ct) => Task.CompletedTask;
    }
}
