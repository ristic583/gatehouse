using System.Net;
using Gatehouse.IntegrationTests.Infrastructure;

namespace Gatehouse.IntegrationTests.Connectors;

public sealed class ConnectorRateLimitTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatewayFactory _factory = new(postgres);

    public async ValueTask InitializeAsync()
    {
        await _factory.InitializeAsync();
        await ConnectorTestEnrollment.ResetAsync(_factory);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Burst_over_50_returns_429_with_retry_after()
    {
        using var cert = await ConnectorTestEnrollment.EnrollAsync(_factory, "nordlys", "HQ-Copenhagen");
        using var client = _factory.CreateClient();
        var responses = await Task.WhenAll(Enumerable.Range(0, 100)
            .Select(_ => ConnectorTestEnrollment.PostAsync(client, "/connector/v1/heartbeat", ConnectorTestEnrollment.NewHeartbeat(), cert)));
        var limited = responses.Where(r => r.StatusCode == HttpStatusCode.TooManyRequests).ToList();
        Assert.NotEmpty(limited);
        Assert.All(limited, r => Assert.True(r.Headers.RetryAfter is not null));
        Assert.True(responses.Count(r => r.StatusCode == HttpStatusCode.OK) <= 70);
    }
}
