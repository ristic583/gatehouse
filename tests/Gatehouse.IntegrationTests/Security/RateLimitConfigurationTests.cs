using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Gatehouse.IntegrationTests.Security;

/// <summary>
/// Security regression (M11, area 3): rate-limit policies on unauthenticated entry points, read from the host configuration (no load).
/// The over-limit behaviour of the magic link redemption is <c>MagicLinkTests.Redeem_is_rate_limited</c>; the web sign-in form limiter
/// is tested in <c>src/web/lib/auth/bff-security.test.ts</c> (SEC-05).
/// </summary>
public sealed class RateLimitConfigurationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Magic_link_redemption_has_a_rate_limit_policy()
    {
        await using var api = new GatehouseApiFactory(postgres);
        await api.InitializeAsync();

        var endpoint = api.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText?.TrimStart('/') == "api/v1/guest/sessions");

        Assert.Equal("guest-sessions", endpoint.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName);
    }

    [Fact]
    public async Task Connector_enrollment_is_rate_limited_for_anonymous_callers()
    {
        await using var gateway = new GatewayFactory(postgres);
        await gateway.InitializeAsync();
        var limiter = gateway.Services.GetRequiredService<IOptions<RateLimiterOptions>>().Value.GlobalLimiter;
        Assert.NotNull(limiter);
        var anonymous = new DefaultHttpContext();
        anonymous.Request.Path = "/connector/v1/enroll";

        // In-memory leases only: no request reaches the host. A bounded limiter refuses well before 1,000 permits.
        var refused = Enumerable.Range(0, 1000).Select(_ => limiter.AttemptAcquire(anonymous)).Count(lease => !lease.IsAcquired);

        Assert.True(refused > 0, "SEC-06: anonymous callers (/enroll) fall into a no-limit partition");
    }
}
