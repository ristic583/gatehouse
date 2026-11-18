using Gatehouse.Agent.Storage;
using Gatehouse.Connector.Contracts.Events;

namespace Gatehouse.UnitTests.Agent;

public sealed class PendingAckStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gh-agent-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    [Fact]
    public async Task Acks_survive_reopen()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        await new PendingAckStore(new AgentDatabase(_dir)).AddAsync(new CommandAcked(id, "k", AckStatus.Applied, "ref", null, null), DateTimeOffset.UnixEpoch, ct);
        var reopened = new PendingAckStore(new AgentDatabase(_dir));
        var list = await reopened.ListAsync(10, ct);
        Assert.Equal(id, Assert.Single(list).CommandId);
        await reopened.RemoveAsync(id, ct);
        Assert.Empty(await reopened.ListAsync(10, ct));
    }
}
