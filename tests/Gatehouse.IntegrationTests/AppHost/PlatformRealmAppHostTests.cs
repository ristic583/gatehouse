using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.AppHost;

[Trait("Category", "AppHost")]
[Collection(AppHostCollection.Name)]
public sealed class PlatformRealmAppHostTests
{
    private const string Realm = "http://localhost:8080/realms/gatehouse-platform";
    private const string Admin = "http://localhost:8080/admin/realms/gatehouse-platform";
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task Platform_realm_is_imported_with_otp_and_audience()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(Timeout);
        var ct = cts.Token;

        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Gatehouse_AppHost>(["DcpPublisher:RandomizePorts=false"], ct);
        await using var app = await builder.BuildAsync(ct);
        await app.StartAsync(ct);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("keycloak", ct);

        using var http = new HttpClient();
        var discovery = await http.GetFromJsonAsync<JsonElement>(new Uri($"{Realm}/.well-known/openid-configuration"), ct);
        Assert.Equal(Realm, discovery.GetProperty("issuer").GetString());

        var keycloak = app.Services.GetRequiredService<DistributedApplicationModel>().Resources.OfType<KeycloakResource>().Single(r => r.Name == "keycloak");
        var password = await keycloak.AdminPasswordParameter.GetValueAsync(ct);
        using var tokenResponse = await http.PostAsync(
            new Uri("http://localhost:8080/realms/master/protocol/openid-connect/token"),
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "admin-cli",
                ["username"] = "admin",
                ["password"] = password!,
            }),
            ct);
        tokenResponse.EnsureSuccessStatusCode();
        var token = (await tokenResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("access_token").GetString();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var actions = await http.GetFromJsonAsync<JsonElement>(new Uri($"{Admin}/authentication/required-actions"), ct);
        Assert.Contains(actions.EnumerateArray(), a =>
            a.GetProperty("alias").GetString() == "CONFIGURE_TOTP" && a.GetProperty("enabled").GetBoolean() && a.GetProperty("defaultAction").GetBoolean());

        var realm = await http.GetFromJsonAsync<JsonElement>(new Uri(Admin), ct);
        Assert.True(realm.GetProperty("bruteForceProtected").GetBoolean());
        Assert.Equal("totp", realm.GetProperty("otpPolicyType").GetString());

        var scopes = await http.GetFromJsonAsync<JsonElement>(new Uri($"{Admin}/client-scopes"), ct);
        Assert.Contains(scopes.EnumerateArray(), s =>
            s.TryGetProperty("protocolMappers", out var mappers)
            && mappers.EnumerateArray().Any(m =>
                m.GetProperty("protocolMapper").GetString() == "oidc-audience-mapper"
                && m.GetProperty("config").GetProperty("included.client.audience").GetString() == "gatehouse-platform-api"));

        var clients = await http.GetFromJsonAsync<JsonElement>(new Uri($"{Admin}/clients"), ct);
        var platformClients = clients.EnumerateArray().Where(c => c.GetProperty("clientId").GetString()!.StartsWith("gatehouse-platform-", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, platformClients.Count);
        // Stricter than the plan: no client of the platform realm (built-ins included) allows password grants, so OTP cannot be bypassed.
        Assert.All(clients.EnumerateArray(), c => Assert.False(c.GetProperty("directAccessGrantsEnabled").GetBoolean(), c.GetProperty("clientId").GetString()));
    }
}
