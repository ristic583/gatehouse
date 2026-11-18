using System.Net;
using Gatehouse.Agent.Transport;
using Gatehouse.Connector.Contracts.Protocol;

namespace Gatehouse.UnitTests.Agent;

public sealed class GatewayClientTests
{
    [Fact]
    public void Backoff_grows_to_60s_with_jitter()
    {
        var backoff = new Backoff(new Random(42));
        var delays = Enumerable.Range(0, 12).Select(_ => backoff.Next().TotalSeconds).ToList();
        Assert.InRange(delays[0], 0.8, 1.2);
        Assert.InRange(delays[3], 6.4, 9.6);
        Assert.All(delays, d => Assert.True(d <= 60));
        Assert.InRange(delays[^1], 48, 60);
        Assert.True(delays.Distinct().Count() > 6);
    }

    [Fact]
    public async Task Unauthorized_stops_polling()
    {
        var client = new GatewayClient(new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden))) { BaseAddress = new Uri("https://gw/") }, "1.0");
        var ex = await Assert.ThrowsAsync<AgentUnauthorizedException>(() => client.PollAsync(new PollRequest(), TestContext.Current.CancellationToken));
        Assert.Equal(403, ex.StatusCode);
    }
}
