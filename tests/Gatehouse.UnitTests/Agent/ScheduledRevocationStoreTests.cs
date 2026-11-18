using Gatehouse.Agent.Storage;

namespace Gatehouse.UnitTests.Agent;

public sealed class ScheduledRevocationStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gh-agent-" + Guid.NewGuid().ToString("N"));
    private readonly ScheduledRevocationStore _store;

    public ScheduledRevocationStoreTests() => _store = new ScheduledRevocationStore(new AgentDatabase(_dir));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    [Fact]
    public async Task Schedule_twice_keeps_one_pending_row()
    {
        await _store.ScheduleAsync("p1", "111111111", "AG-AAR-DOCK", Now.AddDays(1), Ct);
        await _store.ScheduleAsync("p1", "222222222", "AG-AAR-DOCK", Now.AddHours(1), Ct);

        Assert.Equal(1, await _store.PendingCountAsync(Ct));
        var due = Assert.Single(await _store.DueAsync(Now.AddHours(2), 10, Ct));
        Assert.Equal("222222222", due.CardNumber);
        Assert.Equal(Now.AddHours(1), due.RevokeAt);
        Assert.Empty(await _store.DueAsync(Now, 10, Ct));
    }

    [Fact]
    public async Task Cancel_marks_pending_done()
    {
        await _store.ScheduleAsync("p1", "111111111", "AG-AAR-DOCK", Now, Ct);
        await _store.ScheduleAsync("p1", "111111111", "AG-AAR-OFFICE", Now, Ct);

        await _store.CancelAsync("p1", "AG-AAR-DOCK", Ct);

        Assert.Equal(1, await _store.PendingCountAsync(Ct));
        Assert.Equal("AG-AAR-OFFICE", Assert.Single(await _store.DueAsync(Now, 10, Ct)).VendorAccessLevelRef);

        await _store.ScheduleAsync("p1", "111111111", "AG-AAR-DOCK", Now, Ct);
        Assert.Equal(2, await _store.PendingCountAsync(Ct));
    }
}
