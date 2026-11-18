using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gatehouse.Domain.Common;
using Gatehouse.Infrastructure.Identity.Keycloak;
using Gatehouse.Infrastructure.Seeding;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Testcontainers.Keycloak;

namespace Gatehouse.IntegrationTests.Keycloak;

/// <summary>Imports infra/keycloak/realms/gatehouse-realm.json into a real Keycloak and checks its shape.</summary>
public sealed class GatehouseRealmTests : IAsyncLifetime
{
    /// <summary>The reused container is replaced whenever the realm file changes (it is imported only at first start).</summary>
    private static readonly string ReuseKey = "kc26.4-p1-" + Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(RealmFile())))[..16];

    private readonly KeycloakContainer _keycloak = new KeycloakBuilder("quay.io/keycloak/keycloak:26.4")
        .Reuse("keycloak", ReuseKey)
        .WithResourceMapping(new FileInfo(RealmFile()), "/opt/keycloak/data/import/")
        .WithCommand("--import-realm")
        .WithEnvironment("GH_WEB_CLIENT_SECRET", "test-secret")
        .WithEnvironment("GH_BROKER_CLIENT_SECRET", "test-broker-secret")
        .WithEnvironment("UPSTREAM_IDP_INTERNAL_URL", "http://upstream-idp.invalid:8080")
        .WithEnvironment("GH_PROVISIONER_CLIENT_SECRET", ProvisionerSecret)
        .Build();

    private const string ProvisionerSecret = "test-provisioner-secret";

    private HttpClient _http = null!;

    public async ValueTask InitializeAsync()
    {
        await ReusedContainers.RemoveStaleAsync("keycloak", ReuseKey);
        await _keycloak.StartAsync();
        _http = new HttpClient { BaseAddress = new Uri(_keycloak.GetBaseAddress()) };
    }

    public ValueTask DisposeAsync()
    {
        _http.Dispose();
        return ValueTask.CompletedTask; // the reused container keeps running (gatehouse-test-keycloak)
    }

    [Fact]
    public async Task Realm_imports_and_publishes_discovery()
    {
        var ct = TestContext.Current.CancellationToken;

        var discovery = await _http.GetFromJsonAsync<JsonElement>("realms/gatehouse/.well-known/openid-configuration", ct);

        Assert.EndsWith("/realms/gatehouse", discovery.GetProperty("issuer").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Both_organizations_exist_with_domains()
    {
        var ct = TestContext.Current.CancellationToken;

        var orgs = await AdminGetAsync("organizations?briefRepresentation=false", ct);

        var domains = orgs.EnumerateArray().ToDictionary(
            o => o.GetProperty("alias").GetString()!,
            o => o.GetProperty("domains").EnumerateArray().Select(d => d.GetProperty("name").GetString()).ToArray());
        Assert.Equal(["nordlys.test"], domains["nordlys"]);
        Assert.Equal(["bricks.test"], domains["bricks"]);
    }

    [Fact]
    public async Task Web_client_requires_pkce_and_has_tenant_scope()
    {
        var ct = TestContext.Current.CancellationToken;

        var client = (await AdminGetAsync("clients?clientId=gatehouse-web", ct)).EnumerateArray().Single();

        Assert.False(client.GetProperty("publicClient").GetBoolean());
        Assert.False(client.GetProperty("directAccessGrantsEnabled").GetBoolean());
        Assert.Equal("S256", client.GetProperty("attributes").GetProperty("pkce.code.challenge.method").GetString());
        var scopes = client.GetProperty("defaultClientScopes").EnumerateArray().Select(s => s.GetString()).ToArray();
        Assert.Contains("gatehouse-tenant", scopes);
        Assert.Contains("gatehouse-api-audience", scopes);
    }

    [Fact]
    public async Task Tenant_claim_mapper_is_configured()
    {
        var ct = TestContext.Current.CancellationToken;

        var scope = (await AdminGetAsync("client-scopes", ct)).EnumerateArray()
            .Single(s => s.GetProperty("name").GetString() == "gatehouse-tenant");

        Assert.Contains(
            scope.GetProperty("protocolMappers").EnumerateArray(),
            m => m.GetProperty("config").TryGetProperty("claim.name", out var claim) && claim.GetString() == "tenant_id");
    }

    [Fact]
    public async Task Demo_organization_seed_adopts_imported_organization_and_adds_members()
    {
        var ct = TestContext.Current.CancellationToken;
        var nordlys = new TenantId("nordlys");
        // Back to the imported shape: the reused container may have been seeded by an earlier run.
        var org = (await AdminGetAsync("organizations?briefRepresentation=false", ct)).EnumerateArray()
            .Single(o => o.GetProperty("alias").GetString() == "nordlys");
        var orgId = org.GetProperty("id").GetString()!;
        var imported = JsonNode.Parse(org.GetRawText())!;
        imported["name"] = "Nordlys A/S";
        (await AdminSendAsync(HttpMethod.Put, $"organizations/{orgId}", imported, ct)).Dispose();
        var email = $"seed-{Guid.NewGuid():N}@nordlys.test";
        var user = new JsonObject
        {
            ["username"] = email,
            ["email"] = email,
            ["enabled"] = true,
            ["attributes"] = new JsonObject { ["tenant_id"] = new JsonArray("nordlys") },
        };
        using (var created = await AdminSendAsync(HttpMethod.Post, "users", user, ct))
        {
            created.EnsureSuccessStatusCode();
        }

        var options = Options.Create(new KeycloakOptions
        {
            AdminUrl = _keycloak.GetBaseAddress(),
            ProvisionerClientSecret = ProvisionerSecret,
        });
        using var http = new HttpClient();
        using var tokens = new KeycloakAdminTokenCache(options, TimeProvider.System);
        var provisioner = new KeycloakIdentityProvisioner(http, tokens, options, NullLogger<KeycloakIdentityProvisioner>.Instance);
        var seeder = new DemoOrganizationSeeder(provisioner, options);

        var first = await seeder.EnsureAsync(nordlys, "Nordlys A/S", "nordlys.test", ct);
        var second = await seeder.EnsureAsync(nordlys, "Nordlys A/S", "nordlys.test", ct);

        Assert.True(first.IsSuccess, first.IsFailure ? $"{first.Error.Code}: {first.Error.Message}" : null);
        Assert.True(second.IsSuccess, second.IsFailure ? $"{second.Error.Code}: {second.Error.Message}" : null);
        var linked = (await AdminGetAsync("organizations?briefRepresentation=false", ct)).EnumerateArray()
            .Where(o => o.GetProperty("alias").GetString() == "nordlys").ToList();
        Assert.Equal(orgId, Assert.Single(linked).GetProperty("id").GetString());
        Assert.Equal("nordlys", linked[0].GetProperty("name").GetString());
        var members = await AdminGetAsync($"organizations/{orgId}/members?search={Uri.EscapeDataString(email)}&exact=true", ct);
        Assert.Contains(members.EnumerateArray(), m => m.GetProperty("email").GetString() == email);
        Assert.True((await provisioner.SetOrganizationEnabledAsync(nordlys, true, ct)).IsSuccess);
    }

    private async Task<HttpResponseMessage> AdminSendAsync(HttpMethod method, string path, JsonNode body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, new Uri("admin/realms/gatehouse/" + path, UriKind.Relative))
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await AdminTokenAsync(ct));
        return await _http.SendAsync(request, ct);
    }

    private async Task<string> AdminTokenAsync(CancellationToken ct)
    {
        using var tokenResponse = await _http.PostAsync(
            new Uri("realms/master/protocol/openid-connect/token", UriKind.Relative),
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "admin-cli",
                ["username"] = KeycloakBuilder.DefaultUsername,
                ["password"] = KeycloakBuilder.DefaultPassword,
            }),
            ct);
        tokenResponse.EnsureSuccessStatusCode();
        return (await tokenResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("access_token").GetString()!;
    }

    private async Task<JsonElement> AdminGetAsync(string path, CancellationToken ct)
    {
        var token = await AdminTokenAsync(ct);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri("admin/realms/gatehouse/" + path, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }

    private static string RealmFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Gatehouse.slnx")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(
            dir?.FullName ?? throw new InvalidOperationException("Repository root not found."),
            "infra", "keycloak", "realms", "gatehouse-realm.json");
    }
}
