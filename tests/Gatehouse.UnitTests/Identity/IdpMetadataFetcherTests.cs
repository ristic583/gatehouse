using System.Net;
using System.Text;
using Gatehouse.Application.Identity.IdentityProviders;
using Gatehouse.Infrastructure.Identity;
using Microsoft.Extensions.Options;

namespace Gatehouse.UnitTests.Identity;

/// <summary>ADR-009 §4 SSRF rules of the metadata fetcher.</summary>
public sealed class IdpMetadataFetcherTests
{
    private static readonly IPAddress PublicAddress = IPAddress.Parse("93.184.216.34");

    [Fact]
    public async Task Private_address_is_refused()
    {
        var handler = new StubHandler(_ => Json("{}"));
        foreach (var (url, address) in new[]
        {
            ("https://idp.internal.example/.well-known/openid-configuration", IPAddress.Parse("10.1.2.3")),
            ("https://metadata.example/.well-known/openid-configuration", IPAddress.Parse("169.254.169.254")),
            ("https://127.0.0.1/.well-known/openid-configuration", IPAddress.Loopback),
            ("https://v6.example/.well-known/openid-configuration", IPAddress.IPv6Loopback),
        })
        {
            var fetcher = Create(handler, resolve: address);

            var result = await fetcher.FetchOidcDiscoveryAsync(url, TestContext.Current.CancellationToken);

            Assert.True(result.IsFailure, url);
            Assert.Equal("idp.url_not_allowed", result.Error.Code);
        }

        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Redirect_is_refused()
    {
        var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri("http://169.254.169.254/latest/meta-data");
            return response;
        });
        var fetcher = Create(handler);

        var result = await fetcher.FetchOidcDiscoveryAsync("https://idp.example/.well-known/openid-configuration", TestContext.Current.CancellationToken);

        Assert.Equal("idp.url_not_allowed", result.Error.Code);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Http_is_refused_unless_allowed()
    {
        var handler = new StubHandler(req => req.RequestUri!.AbsolutePath.EndsWith("jwks", StringComparison.Ordinal)
            ? Json("""{"keys":[{"kty":"RSA","use":"sig","n":"x","e":"AQAB"}]}""")
            : Json("""{"issuer":"http://localhost:8081/realms/acme","authorization_endpoint":"http://localhost:8081/a","token_endpoint":"http://localhost:8081/t","jwks_uri":"http://localhost:8081/jwks"}"""));

        var refused = await Create(handler).FetchOidcDiscoveryAsync("http://idp.example/.well-known/openid-configuration", TestContext.Current.CancellationToken);
        Assert.Equal("idp.url_not_allowed", refused.Error.Code);
        Assert.Equal(0, handler.Calls);

        var allowed = await Create(handler, allowedHosts: ["localhost:8081"])
            .FetchOidcDiscoveryAsync("http://localhost:8081/realms/acme/.well-known/openid-configuration", TestContext.Current.CancellationToken);
        Assert.True(allowed.IsSuccess);
        Assert.Equal("http://localhost:8081/realms/acme", allowed.Value.Issuer);
        Assert.Equal(1, allowed.Value.SigningKeys);

        var otherPort = await Create(handler, allowedHosts: ["localhost:8081"])
            .FetchOidcDiscoveryAsync("http://localhost:9999/.well-known/openid-configuration", TestContext.Current.CancellationToken);
        Assert.Equal("idp.url_not_allowed", otherPort.Error.Code);
    }

    [Fact]
    public async Task Internal_token_or_userinfo_endpoint_is_refused()
    {
        foreach (var (token, userInfo) in new[]
        {
            ("http://127.0.0.1:8080/token", "https://idp.example/userinfo"),
            ("https://idp.example/token", "https://169.254.169.254/latest/meta-data"),
            ("https://internal.example/token", "https://idp.example/userinfo"),
        })
        {
            var handler = new StubHandler(req => req.RequestUri!.AbsolutePath.EndsWith("jwks", StringComparison.Ordinal)
                ? Json("""{"keys":[{"kty":"RSA","use":"sig","n":"x","e":"AQAB"}]}""")
                : Json($$"""{"issuer":"https://idp.example","authorization_endpoint":"https://idp.example/a","token_endpoint":"{{token}}","userinfo_endpoint":"{{userInfo}}","jwks_uri":"https://idp.example/jwks"}"""));
            var fetcher = new IdpMetadataFetcher(new HttpClient(handler), Options.Create(new IdentityProviderOptions()),
                (host, _) => Task.FromResult(new[] { host == "internal.example" ? IPAddress.Parse("10.0.0.5") : PublicAddress }));

            var result = await fetcher.FetchOidcDiscoveryAsync("https://idp.example/.well-known/openid-configuration", TestContext.Current.CancellationToken);

            Assert.True(result.IsFailure, token + " " + userInfo);
            Assert.Equal("idp.url_not_allowed", result.Error.Code);
        }
    }

    [Fact]
    public async Task Oversize_metadata_is_refused()
    {
        var big = new string(' ', IdpMetadataFetcher.MaxBytes + 10);
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(big))) });

        var result = await Create(handler).ParseSamlMetadataAsync("https://idp.example/metadata", null, TestContext.Current.CancellationToken);

        Assert.Equal("idp.metadata_invalid", result.Error.Code);
    }

    [Fact]
    public async Task Saml_dtd_is_refused()
    {
        const string Xml = """
            <?xml version="1.0"?>
            <!DOCTYPE foo [ <!ENTITY xxe SYSTEM "file:///etc/passwd"> ]>
            <md:EntityDescriptor xmlns:md="urn:oasis:names:tc:SAML:2.0:metadata" entityID="&xxe;">
              <md:IDPSSODescriptor protocolSupportEnumeration="urn:oasis:names:tc:SAML:2.0:protocol"/>
            </md:EntityDescriptor>
            """;

        var result = await Create(new StubHandler(_ => Json("{}"))).ParseSamlMetadataAsync(null, Xml, TestContext.Current.CancellationToken);

        Assert.True(result.IsFailure);
        Assert.Equal("idp.metadata_invalid", result.Error.Code);
    }

    private static IdpMetadataFetcher Create(StubHandler handler, IPAddress? resolve = null, string[]? allowedHosts = null)
    {
        var options = new IdentityProviderOptions();
        foreach (var host in allowedHosts ?? [])
        {
            options.AllowedInsecureHosts.Add(host);
        }

        var address = resolve ?? PublicAddress;
        return new IdpMetadataFetcher(new HttpClient(handler), Options.Create(options), (_, _) => Task.FromResult(new[] { address }));
    }

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
