using Gatehouse.Domain.Connectors.Events;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.Sites;

namespace Gatehouse.UnitTests.Fulfillment;

public sealed class PacsInstanceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Heartbeat_older_than_90_seconds_is_offline()
    {
        var pacs = PacsInstance.Create("HQ", PacsVendor.LegacyPacs, [SiteId.New()]).Value;
        Assert.False(pacs.IsOnline(Now));

        pacs.RecordHeartbeat(Now, "1.0.0", null);
        Assert.True(pacs.IsOnline(Now.AddSeconds(90)));
        Assert.False(pacs.IsOnline(Now.AddSeconds(91)));
    }

    private static PacsInstance NewPacs() => PacsInstance.Create("HQ", PacsVendor.LegacyPacs, [SiteId.New()]).Value;

    [Fact]
    public void Heartbeat_after_offline_raises_came_online()
    {
        var pacs = NewPacs();
        pacs.RecordHeartbeat(Now, "1.0.0", null);
        pacs.ClearDomainEvents();
        pacs.RecordHeartbeat(Now.AddMinutes(10), "1.0.0", null);
        var e = Assert.IsType<ConnectorCameOnline>(Assert.Single(pacs.DomainEvents));
        Assert.Equal(Now, e.OfflineSince);
    }

    [Fact]
    public void Heartbeat_while_online_raises_nothing()
    {
        var pacs = NewPacs();
        pacs.RecordHeartbeat(Now, "1.0.0", null);
        pacs.ClearDomainEvents();
        pacs.RecordHeartbeat(Now.AddSeconds(15), "1.0.0", null);
        Assert.Empty(pacs.DomainEvents);
    }

    [Fact]
    public void Heartbeat_reports_capability_change()
    {
        var pacs = NewPacs();
        Assert.True(pacs.RecordHeartbeat(Now, "1.0.0", "{\"a\":1}"));
        Assert.False(pacs.RecordHeartbeat(Now.AddSeconds(15), "1.0.0", "{\"a\":1}"));
        Assert.True(pacs.RecordHeartbeat(Now.AddSeconds(30), "1.0.0", "{\"a\":2}"));
    }

    [Fact]
    public void Mark_offline_raises_once_until_next_heartbeat()
    {
        var pacs = NewPacs();
        pacs.SetFulfillmentMode(FulfillmentMode.Connector);
        Assert.False(pacs.MarkOfflineIfStale(Now));
        pacs.RecordHeartbeat(Now, "1.0.0", null);
        pacs.ClearDomainEvents();

        Assert.False(pacs.MarkOfflineIfStale(Now.AddSeconds(60)));
        Assert.True(pacs.MarkOfflineIfStale(Now.AddMinutes(5)));
        var e = Assert.IsType<ConnectorWentOffline>(Assert.Single(pacs.DomainEvents));
        Assert.Equal(Now, e.LastHeartbeatAt);
        Assert.False(pacs.MarkOfflineIfStale(Now.AddMinutes(6)));

        pacs.RecordHeartbeat(Now.AddMinutes(7), "1.0.0", null);
        Assert.True(pacs.MarkOfflineIfStale(Now.AddMinutes(20)));

        var manual = NewPacs();
        manual.RecordHeartbeat(Now, "1.0.0", null);
        Assert.False(manual.MarkOfflineIfStale(Now.AddMinutes(5)));
    }
}
