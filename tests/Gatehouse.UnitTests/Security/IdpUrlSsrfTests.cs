using System.Net;
using System.Text;
using Gatehouse.Application.Identity.IdentityProviders;
using Gatehouse.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace Gatehouse.UnitTests.Security;

/// <summary>
/// Security regression (M11, area 4): identity provider URLs that reach loopback, link-local or private ranges are refused by
/// <see cref="IdpMetadataFetcher"/>, before the request, after a redirect, and again at connect time (DNS rebinding). No network.
/// </summary>
public sealed class IdpUrlSsrfTests
{
    public static TheoryData<string> NonPublicAddresses =>
    [
        "127.0.0.1", "127.8.9.10", "0.0.0.0", "10.0.0.1", "172.16.0.1", "172.31.255.254", "192.168.1.1", "169.254.169.254",
        "100.64.0.1", "198.18.0.1", "224.0.0.1", "255.255.255.255", "::1", "::", "fe80::1", "fc00::1", "fd12:3456::1",
        "::ffff:127.0.0.1", "::ffff:10.0.0.1", "::ffff:169.254.169.254",
    ];

    /// <summary>IPv6 forms that embed a private IPv4 address and reach it through a translator or tunnel.</summary>
    public static TheoryData<string> EmbeddedIpv4Addresses => ["64:ff9b::a9fe:a9fe", "64:ff9b::a00:1", "2002:a9fe:a9fe::1", "::a9fe:a9fe", "::ffff:0:a9fe:a9fe", "64:ff9b:1::a9fe:a9fe"];

    [Theory]
    [MemberData(nameof(NonPublicAddresses))]
    public async Task Host_resolving_to_non_public_address_is_refused_before_any_request(string address)
    {
        var handler = new StubHandler(_ => Json("{}"));
        var fetcher = Create(handler, _ => [IPAddress.Parse(address)]);

        var result = await fetcher.FetchOidcDiscoveryAsync("https://idp.example/.well-known/openid-configuration", TestContext.Current.CancellationToken);

        Assert.Equal("idp.url_not_allowed", result.Error.Code);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [MemberData(nameof(EmbeddedIpv4Addresses))]
    public async Task Ipv6_address_embedding_a_private_ipv4_address_is_refused(string address)
    {
        var handler = new StubHandler(_ => Json("{}"));
        var fetcher = Create(handler, _ => [IPAddress.Parse(address)]);

        var result = await fetcher.FetchOidcDiscoveryAsync("https://idp.example/.well-known/openid-configuration", TestContext.Current.CancellationToken);

        Assert.Equal("idp.url_not_allowed", result.Error.Code);
    }

    [Theory]
    [InlineData("https://localhost/.well-known/openid-configuration")]
    [InlineData("https://127.0.0.1/.well-known/openid-configuration")]
    [InlineData("https://2130706433/.well-known/openid-configuration")]
    [InlineData("https://0x7f000001/.well-known/openid-configuration")]
    [InlineData("https://[::ffff:127.0.0.1]/.well-known/openid-configuration")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    public async Task Literal_and_encoded_loopback_urls_are_refused(string url)
    {
        var handler = new StubHandler(_ => Json("{}"));
        var fetcher = Create(handler, LikeTheOsResolver);

        var result = await fetcher.FetchOidcDiscoveryAsync(url, TestContext.Current.CancellationToken);

        Assert.Equal("idp.url_not_allowed", result.Error.Code);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.MovedPermanently, "http://127.0.0.1/admin")]
    [InlineData(HttpStatusCode.TemporaryRedirect, "https://10.0.0.5/.well-known/openid-configuration")]
    [InlineData(HttpStatusCode.PermanentRedirect, "https://idp-other.example/.well-known/openid-configuration")]
    [InlineData(HttpStatusCode.SeeOther, "http://[::1]/")]
    public async Task Redirects_are_never_followed(HttpStatusCode status, string location)
    {
        var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(status);
            response.Headers.Location = new Uri(location);
            return response;
        });
        var fetcher = Create(handler, _ => [IPAddress.Parse("93.184.216.34")]);

        var result = await fetcher.FetchOidcDiscoveryAsync("https://idp.example/.well-known/openid-configuration", TestContext.Current.CancellationToken);

        Assert.Equal("idp.url_not_allowed", result.Error.Code);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Saml_metadata_url_on_private_address_is_refused()
    {
        var handler = new StubHandler(_ => Json("{}"));
        var fetcher = Create(handler, _ => [IPAddress.Parse("192.168.0.10")]);

        var result = await fetcher.ParseSamlMetadataAsync("https://saml.example/metadata", null, TestContext.Current.CancellationToken);

        Assert.Equal("idp.url_not_allowed", result.Error.Code);
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("https://127.0.0.1:9/x")]
    [InlineData("https://localhost:9/x")]
    [InlineData("https://[::1]:9/x")]
    public async Task Connect_time_check_refuses_loopback_after_dns(string url)
    {
        using var handler = IdpMetadataFetcher.CreateHandler(new IdentityProviderOptions());
        using var client = new HttpClient(handler);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync(new Uri(url), TestContext.Current.CancellationToken));

        Assert.Contains("idp.url_not_allowed", error.ToString(), StringComparison.Ordinal);
    }

    private static IPAddress[] LikeTheOsResolver(string host) =>
        IPAddress.TryParse(host, out var literal) ? [literal]
        : host == "localhost" ? [IPAddress.Loopback, IPAddress.IPv6Loopback]
        : [IPAddress.Parse("93.184.216.34")];

    private static IdpMetadataFetcher Create(StubHandler handler, Func<string, IPAddress[]> resolve) =>
        new(new HttpClient(handler), Options.Create(new IdentityProviderOptions()), (host, _) => Task.FromResult(resolve(host)));

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(respond(request));
        }
    }
}
