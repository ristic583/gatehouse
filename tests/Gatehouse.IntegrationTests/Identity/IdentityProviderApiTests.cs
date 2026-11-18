extern alias GatehouseApi;

using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Gatehouse.Application.Identity.IdentityProviders;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tenancy;
using Gatehouse.Infrastructure.Persistence.Catalog;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Identity;

/// <summary>ADR-009 §4 tenant-admin IdP management against a stub IdP on an allowed localhost port.</summary>
public sealed class IdentityProviderApiTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Secret = "upstream-client-secret-4242";
    private static readonly Uri Endpoint = new("/api/v1/admin/identity-provider", UriKind.Relative);
    private readonly GatehouseApiFactory _api = new(postgres);
    private StubIdp _idp = null!;
    private WebApplicationFactory<GatehouseApi::Program> _host = null!;

    public async ValueTask InitializeAsync()
    {
        await _api.InitializeAsync();
        _idp = StubIdp.Start();
        _host = _api.WithWebHostBuilder(b => b.UseSetting("Identity:AllowedInsecureHosts:0", $"localhost:{_idp.Port}"));
    }

    public async ValueTask DisposeAsync()
    {
        _idp.Dispose();
        await _host.DisposeAsync();
        await _api.DisposeAsync();
    }

    private FakeIdentityProvisioner Identity => _host.Services.GetRequiredService<FakeIdentityProvisioner>();

    [Fact]
    public async Task Save_oidc_provider_links_to_own_organization()
    {
        var ct = TestContext.Current.CancellationToken;
        using var freja = Client(DemoUser.Freja);

        using var response = await freja.PutAsJsonAsync(Endpoint, Oidc(_idp.DiscoveryUrl), ApiJson.Options, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = (await response.Content.ReadFromJsonAsync<IdentityProviderDto>(ApiJson.Options, ct))!;
        Assert.Equal(IdpKind.Oidc, dto.Kind);
        Assert.Equal("nordlys-oidc", dto.Alias);
        Assert.Equal(_idp.Issuer, dto.Issuer);
        Assert.Contains("/broker/nordlys-oidc/endpoint", dto.BrokerRedirectUri, StringComparison.Ordinal);
        Assert.True(dto.LastTestOk);
        Assert.True(Identity.Providers.ContainsKey(new TenantId("nordlys")));
        Assert.Equal("nordlys-oidc", Identity.Providers[new TenantId("nordlys")].Alias);
        Assert.Equal(IdpKind.Oidc, await CatalogIdpKindAsync("nordlys", ct));
    }

    [Fact]
    public async Task Failed_check_refuses_save()
    {
        var ct = TestContext.Current.CancellationToken;
        using var mia = Client(DemoUser.Mia);
        var callsBefore = Identity.Calls.Count(c => c.Contains("Upsert", StringComparison.Ordinal));

        // The stub serves a discovery document whose issuer does not match this URL.
        using var response = await mia.PutAsJsonAsync(Endpoint, Oidc(_idp.WrongIssuerUrl), ApiJson.Options, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("idp.invalid_config", await response.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
        Assert.Equal(callsBefore, Identity.Calls.Count(c => c.Contains("Upsert", StringComparison.Ordinal)));

        using var test = await mia.PostAsJsonAsync(new Uri("/api/v1/admin/identity-provider/test", UriKind.Relative), Oidc(_idp.WrongIssuerUrl), ApiJson.Options, ct);
        Assert.Equal(HttpStatusCode.OK, test.StatusCode);
        var result = (await test.Content.ReadFromJsonAsync<IdpTestResultDto>(ApiJson.Options, ct))!;
        Assert.False(result.Ok);
        Assert.Contains(result.Checks, c => c.Name == IdentityProviderChecks.IssuerMatches && !c.Ok);

        using var privateUrl = await mia.PostAsJsonAsync(new Uri("/api/v1/admin/identity-provider/test", UriKind.Relative), Oidc("https://10.0.0.1/.well-known/openid-configuration"), ApiJson.Options, ct);
        Assert.Equal(HttpStatusCode.BadRequest, privateUrl.StatusCode);
        Assert.Contains("idp.url_not_allowed", await privateUrl.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Secret_is_never_returned()
    {
        var ct = TestContext.Current.CancellationToken;
        using var freja = Client(DemoUser.Freja);
        using var saved = await freja.PutAsJsonAsync(Endpoint, Oidc(_idp.DiscoveryUrl), ApiJson.Options, ct);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.DoesNotContain(Secret, await saved.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);

        using var get = await freja.GetAsync(Endpoint, ct);
        var json = await get.Content.ReadAsStringAsync(ct);
        Assert.DoesNotContain(Secret, json, StringComparison.Ordinal);
        Assert.True((await get.Content.ReadFromJsonAsync<IdentityProviderDto>(ApiJson.Options, ct))!.HasClientSecret);

        // Update without a secret keeps the stored one.
        using var update = await freja.PutAsJsonAsync(Endpoint, Oidc(_idp.DiscoveryUrl, secret: null), ApiJson.Options, ct);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        await using var scope = await _api.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var setting = await db.TenantSettings.AsNoTracking().SingleAsync(s => s.Key == IdentityProviderStore.Key, ct);
        Assert.DoesNotContain(Secret, setting.Value, StringComparison.Ordinal);
        var payloads = await db.AuditEntries.AsNoTracking().Where(e => e.Action.StartsWith("identity_provider.")).Select(e => e.Payload).ToListAsync(ct);
        Assert.NotEmpty(payloads);
        Assert.All(payloads, p => Assert.DoesNotContain(Secret, p, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Failed_kind_switch_keeps_working_provider()
    {
        var ct = TestContext.Current.CancellationToken;
        using var freja = Client(DemoUser.Freja);
        using var saved = await freja.PutAsJsonAsync(Endpoint, Oidc(_idp.DiscoveryUrl), ApiJson.Options, ct);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Identity.FailNext(nameof(FakeIdentityProvisioner.UpsertSamlProviderAsync));
        var (xml, _) = SamlMetadataXml("https://idp.bricks.example/saml", "https://idp.bricks.example/sso");

        using var response = await freja.PutAsJsonAsync(Endpoint,
            new SaveIdentityProviderRequest(IdpKind.Saml, "Bricks ADFS", null, new SamlSettingsRequest(null, xml)), ApiJson.Options, ct);

        Assert.False(response.IsSuccessStatusCode);
        Assert.DoesNotContain(Identity.Calls, c => c.StartsWith(nameof(FakeIdentityProvisioner.RemoveProviderAsync), StringComparison.Ordinal));
        Assert.Equal(IdpKind.Oidc, Identity.Providers[new TenantId("nordlys")].Kind);
        Assert.Equal(IdpKind.Oidc, await CatalogIdpKindAsync("nordlys", ct));
    }

    [Fact]
    public async Task Saml_metadata_xml_is_parsed()
    {
        var ct = TestContext.Current.CancellationToken;
        using var mia = Client(DemoUser.Mia);
        var (xml, thumbprint) = SamlMetadataXml("https://idp.bricks.example/saml", "https://idp.bricks.example/sso");

        using var response = await mia.PutAsJsonAsync(Endpoint,
            new SaveIdentityProviderRequest(IdpKind.Saml, "Bricks ADFS", null, new SamlSettingsRequest(null, xml)), ApiJson.Options, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = (await response.Content.ReadFromJsonAsync<IdentityProviderDto>(ApiJson.Options, ct))!;
        Assert.Equal(IdpKind.Saml, dto.Kind);
        Assert.Equal("https://idp.bricks.example/saml", dto.SamlEntityId);
        Assert.Equal("https://idp.bricks.example/sso", dto.SamlSsoUrl);
        Assert.Equal(thumbprint, dto.SigningCertificateThumbprint);
        Assert.NotNull(dto.SpEntityId);
        Assert.False(dto.HasClientSecret);
    }

    [Fact]
    public async Task Non_admin_is_403()
    {
        var ct = TestContext.Current.CancellationToken;
        using var anna = Client(DemoUser.Anna);

        using var put = await anna.PutAsJsonAsync(Endpoint, Oidc(_idp.DiscoveryUrl), ApiJson.Options, ct);
        using var get = await anna.GetAsync(Endpoint, ct);

        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, get.StatusCode);
    }

    [Fact]
    public async Task Remove_sets_idp_kind_local()
    {
        var ct = TestContext.Current.CancellationToken;
        using var freja = Client(DemoUser.Freja);
        using var saved = await freja.PutAsJsonAsync(Endpoint, Oidc(_idp.DiscoveryUrl), ApiJson.Options, ct);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        using var delete = await freja.DeleteAsync(Endpoint, ct);

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Equal(IdpKind.Local, await CatalogIdpKindAsync("nordlys", ct));
        Assert.False(Identity.Providers.ContainsKey(new TenantId("nordlys")));
        using var get = await freja.GetAsync(Endpoint, ct);
        Assert.Equal(HttpStatusCode.NoContent, get.StatusCode);
        using var again = await freja.DeleteAsync(Endpoint, ct);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
    }

    private static SaveIdentityProviderRequest Oidc(string discoveryUrl, string? secret = Secret) =>
        new(IdpKind.Oidc, "Upstream SSO", new OidcSettingsRequest(discoveryUrl, "gatehouse", secret, null), null);

    private HttpClient Client(DemoUser user)
    {
        var client = _host.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubHeader, user.Subject);
        client.DefaultRequestHeaders.Add(TestAuthHandler.EmailHeader, user.Email);
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantHeader, user.Tenant);
        return client;
    }

    private async Task<IdpKind> CatalogIdpKindAsync(string tenant, CancellationToken ct)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var id = new TenantId(tenant);
        return await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().Tenants.AsNoTracking().Where(t => t.Id == id).Select(t => t.IdpKind).SingleAsync(ct);
    }

    private static (string Xml, string Thumbprint) SamlMetadataXml(string entityId, string ssoUrl)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=bricks-idp", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var b64 = Convert.ToBase64String(cert.RawData);
        var xml = $"""
            <md:EntityDescriptor xmlns:md="urn:oasis:names:tc:SAML:2.0:metadata" xmlns:ds="http://www.w3.org/2000/09/xmldsig#" entityID="{entityId}">
              <md:IDPSSODescriptor protocolSupportEnumeration="urn:oasis:names:tc:SAML:2.0:protocol">
                <md:KeyDescriptor use="signing"><ds:KeyInfo><ds:X509Data><ds:X509Certificate>{b64}</ds:X509Certificate></ds:X509Data></ds:KeyInfo></md:KeyDescriptor>
                <md:SingleSignOnService Binding="urn:oasis:names:tc:SAML:2.0:bindings:HTTP-POST" Location="{ssoUrl}"/>
              </md:IDPSSODescriptor>
            </md:EntityDescriptor>
            """;
        return (xml, cert.GetCertHashString(HashAlgorithmName.SHA256).ToLowerInvariant());
    }

    /// <summary>Minimal OIDC discovery + JWKS on <c>http://localhost:{port}</c>.</summary>
    private sealed class StubIdp : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _stop = new();

        private StubIdp(int port)
        {
            Port = port;
            _listener.Prefixes.Add($"http://localhost:{port}/");
            _listener.Start();
            _ = Task.Run(LoopAsync);
        }

        public int Port { get; }

        public string Issuer => $"http://localhost:{Port}/realms/acme";

        public string DiscoveryUrl => $"{Issuer}/.well-known/openid-configuration";

        public string WrongIssuerUrl => $"http://localhost:{Port}/wrong/.well-known/openid-configuration";

        public static StubIdp Start()
        {
            using var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return new StubIdp(port);
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Close();
            _stop.Dispose();
        }

        private async Task LoopAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or InvalidOperationException)
                {
                    return;
                }

                var path = context.Request.Url!.AbsolutePath;
                var body = path.EndsWith("/jwks", StringComparison.Ordinal)
                    ? """{"keys":[{"kty":"RSA","use":"sig","kid":"k1","n":"sXchY","e":"AQAB"}]}"""
                    : path.EndsWith("/.well-known/openid-configuration", StringComparison.Ordinal)
                        ? $$"""{"issuer":"{{Issuer}}","authorization_endpoint":"{{Issuer}}/auth","token_endpoint":"{{Issuer}}/token","jwks_uri":"{{Issuer}}/jwks","userinfo_endpoint":"{{Issuer}}/userinfo"}"""
                        : null;
                context.Response.StatusCode = body is null ? 404 : 200;
                context.Response.ContentType = "application/json";
                var bytes = Encoding.UTF8.GetBytes(body ?? "{}");
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
            }
        }
    }
}
