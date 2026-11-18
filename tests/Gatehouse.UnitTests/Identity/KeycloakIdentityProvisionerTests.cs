using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Gatehouse.Application.Tenancy.IdentityProvisioning;
using Gatehouse.Domain.Common;
using Gatehouse.Infrastructure.Identity.Keycloak;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Gatehouse.UnitTests.Identity;

public sealed class KeycloakIdentityProvisionerTests
{
    private const string Secret = "super-secret-client-value";
    private static readonly TenantId Acme = new("acme");

    [Fact]
    public void Invite_path_returns_to_gatehouse_when_redirect_is_configured()
    {
        var plain = KeycloakIdentityProvisioner.InviteActionsPath(new KeycloakOptions { InviteLifespanSeconds = 60 }, "u-1");
        var redirect = KeycloakIdentityProvisioner.InviteActionsPath(
            new KeycloakOptions { InviteLifespanSeconds = 60, InviteRedirectUri = "https://gh.test/auth/callback" }, "u-1");

        Assert.Equal("users/u-1/execute-actions-email?lifespan=60", plain);
        Assert.Equal("users/u-1/execute-actions-email?lifespan=60&client_id=gatehouse-web&redirect_uri=https%3A%2F%2Fgh.test%2Fauth%2Fcallback", redirect);
    }

    [Fact]
    public async Task Saml_provider_always_validates_signatures()
    {
        var keycloak = new StubKeycloak();
        var result = await keycloak.Provisioner().UpsertSamlProviderAsync(Acme, Saml(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var created = keycloak.Body("POST", "/identity-provider/instances")!;
        Assert.Equal("acme-saml", (string?)created["alias"]);
        Assert.Equal("true", (string?)created["config"]!["validateSignature"]);
        Assert.Equal("true", (string?)created["config"]!["wantAssertionsSigned"]);
        Assert.Equal("MIIBcert", (string?)created["config"]!["signingCertificate"]);
        Assert.Equal("/realms/gatehouse", new Uri(result.Value.SpEntityId!).AbsolutePath);
    }

    [Fact]
    public async Task Backchannel_rewrite_applies_to_token_and_jwks_but_not_authorization()
    {
        var keycloak = new StubKeycloak();
        var spec = Oidc() with
        {
            AuthorizationUrl = "http://localhost:8081/realms/acme/auth",
            TokenUrl = "http://localhost:8081/realms/acme/token",
            JwksUrl = "http://localhost:8081/realms/acme/certs",
        };

        var result = await keycloak.Provisioner("http://localhost:8081=http://upstream-idp:8080").UpsertOidcProviderAsync(Acme, spec, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var config = keycloak.Body("POST", "/identity-provider/instances")!["config"]!;
        Assert.Equal("http://upstream-idp:8080/realms/acme/token", (string?)config["tokenUrl"]);
        Assert.Equal("http://upstream-idp:8080/realms/acme/certs", (string?)config["jwksUrl"]);
        Assert.Equal("http://localhost:8081/realms/acme/auth", (string?)config["authorizationUrl"]);
    }

    [Fact]
    public async Task Tenant_mapper_value_comes_from_tenant_argument()
    {
        var keycloak = new StubKeycloak();
        // The display name (request input) claims another tenant; only the TenantId argument may set tenant_id.
        var spec = Oidc() with { DisplayName = "nordlys" };

        var result = await keycloak.Provisioner().UpsertOidcProviderAsync(Acme, spec, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("acme-oidc", result.Value.Alias);
        var mapper = keycloak.Bodies("POST", "/identity-provider/instances/acme-oidc/mappers").Single(m => (string?)m["name"] == "tenant_id");
        Assert.Equal("acme", (string?)mapper["config"]!["attribute.value"]);
        Assert.Equal("FORCE", (string?)mapper["config"]!["syncMode"]);
        Assert.Contains(keycloak.Requests, r => r == "POST /organizations/org-1/identity-providers");
    }

    [Fact]
    public async Task Switching_kind_removes_previous_provider()
    {
        var keycloak = new StubKeycloak();
        keycloak.ExistingProviders.Add("acme-oidc");

        var result = await keycloak.Provisioner().UpsertSamlProviderAsync(Acme, Saml(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("DELETE /organizations/org-1/identity-providers/acme-oidc", keycloak.Requests);
        Assert.Contains("DELETE /identity-provider/instances/acme-oidc", keycloak.Requests);
        Assert.Contains("POST /identity-provider/instances", keycloak.Requests);
    }

    [Fact]
    public async Task Failed_kind_switch_keeps_previous_provider()
    {
        var keycloak = new StubKeycloak
        {
            Override = r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/identity-provider/instances", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{\"errorMessage\":\"invalid\"}") }
                : null,
        };
        keycloak.ExistingProviders.Add("acme-oidc");

        var result = await keycloak.Provisioner().UpsertSamlProviderAsync(Acme, Saml(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.DoesNotContain(keycloak.Requests, r => r.StartsWith("DELETE", StringComparison.Ordinal));
        Assert.Contains("acme-oidc", keycloak.ExistingProviders);
    }

    [Fact]
    public async Task Keycloak_error_does_not_leak_secret()
    {
        var keycloak = new StubKeycloak
        {
            Override = r => r.Method == HttpMethod.Post && r.RequestUri!.AbsolutePath.EndsWith("/identity-provider/instances", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent($"{{\"errorMessage\":\"invalid clientSecret {Secret}\"}}") }
                : null,
        };

        var result = await keycloak.Provisioner().UpsertOidcProviderAsync(Acme, Oidc(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("idp.provisioning_failed", result.Error.Code);
        Assert.DoesNotContain(Secret, result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Token_is_cached_until_expiry()
    {
        var keycloak = new StubKeycloak();
        var provisioner = keycloak.Provisioner();

        await provisioner.GetProviderAsync(Acme, CancellationToken.None);
        await provisioner.GetProviderAsync(Acme, CancellationToken.None);
        Assert.Equal(1, keycloak.TokenRequests);

        keycloak.Time.Advance(TimeSpan.FromSeconds(269));
        await provisioner.GetProviderAsync(Acme, CancellationToken.None);
        Assert.Equal(1, keycloak.TokenRequests);

        keycloak.Time.Advance(TimeSpan.FromSeconds(2)); // 300 s lifetime - 30 s margin passed
        await provisioner.GetProviderAsync(Acme, CancellationToken.None);
        Assert.Equal(2, keycloak.TokenRequests);
    }

    private static OidcProviderSpec Oidc() => new(
        "Acme", "https://idp.acme.test", "https://idp.acme.test/auth", "https://idp.acme.test/token",
        "https://idp.acme.test/jwks", null, "gatehouse", Secret, ["openid", "email"]);

    private static SamlProviderSpec Saml() => new(
        "Acme", "https://idp.acme.test/saml", "https://idp.acme.test/sso", "-----BEGIN CERTIFICATE-----\nMIIB\ncert\n-----END CERTIFICATE-----\n");

    private sealed class StubKeycloak : HttpMessageHandler
    {
        private readonly List<(string Key, JsonNode? Body)> _log = [];

        public FakeTimeProvider Time { get; } = new();

        public HashSet<string> ExistingProviders { get; } = [];

        public Func<HttpRequestMessage, HttpResponseMessage?>? Override { get; init; }

        public int TokenRequests { get; private set; }

        public IEnumerable<string> Requests => _log.Select(l => l.Key);

        public KeycloakIdentityProvisioner Provisioner(params string[] backchannelRewrites)
        {
            var settings = new KeycloakOptions { AdminUrl = "http://kc", ProvisionerClientSecret = "provisioner" };
            foreach (var rule in backchannelRewrites)
            {
                settings.BackchannelRewrites.Add(rule);
            }

            var options = Options.Create(settings);
#pragma warning disable CA2000 // The HttpClient owns the handler for the test's lifetime.
            var http = new HttpClient(this, disposeHandler: false);
#pragma warning restore CA2000
            return new KeycloakIdentityProvisioner(http, new KeycloakAdminTokenCache(options, Time), options, NullLogger<KeycloakIdentityProvisioner>.Instance);
        }

        public JsonNode? Body(string method, string path) => Bodies(method, path).FirstOrDefault();

        public IEnumerable<JsonNode> Bodies(string method, string path) =>
            _log.Where(l => l.Key == $"{method} {path}" && l.Body is not null).Select(l => l.Body!);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/protocol/openid-connect/token", StringComparison.Ordinal))
            {
                TokenRequests++;
                return Json("""{"access_token":"t","expires_in":300}""");
            }

            var relative = path.Replace("/admin/realms/gatehouse", string.Empty, StringComparison.Ordinal);
            var text = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            _log.Add(($"{request.Method} {relative}", text is { Length: > 0 } ? JsonNode.Parse(text) : null));

            if (Override?.Invoke(request) is { } custom)
            {
                return custom;
            }

            if (request.Method == HttpMethod.Get && relative == "/organizations")
            {
                return Json("""[{"id":"org-1","name":"acme","alias":"acme","enabled":true,"domains":[{"name":"acme.test","verified":true}]}]""");
            }

            if (request.Method == HttpMethod.Get && relative.EndsWith("/mappers", StringComparison.Ordinal))
            {
                return Json("[]");
            }

            if (request.Method == HttpMethod.Get && relative.StartsWith("/identity-provider/instances/", StringComparison.Ordinal))
            {
                var alias = relative["/identity-provider/instances/".Length..];
                return ExistingProviders.Contains(alias) ? Json($$"""{"alias":"{{alias}}","internalId":"i-1"}""") : new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            if (request.Method == HttpMethod.Delete && relative.StartsWith("/identity-provider/instances/", StringComparison.Ordinal))
            {
                ExistingProviders.Remove(relative["/identity-provider/instances/".Length..]);
            }

            return new HttpResponseMessage(request.Method == HttpMethod.Post ? HttpStatusCode.Created : HttpStatusCode.NoContent);
        }

        private static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
