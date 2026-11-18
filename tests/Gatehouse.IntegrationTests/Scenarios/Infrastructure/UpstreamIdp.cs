using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Scenarios.Infrastructure;

/// <summary>The local upstream IdP (http://localhost:8081): realm <c>acme-oidc</c> and its broker client for fresh tenants.</summary>
public static class UpstreamIdp
{
    public const string AcmeRealm = "acme-oidc";
    public const string AcmeClientId = "gatehouse-broker-acme";
    private static readonly Uri BaseUrl = new("http://localhost:8081/");

    public static string AcmeDiscoveryUrl => $"{BaseUrl}realms/{AcmeRealm}/.well-known/openid-configuration";

    /// <summary>Creates a user in <c>acme-oidc</c>; the username is the email's local part (what <see cref="ScenarioClient"/>
    /// types on 8081).</summary>
    public static async Task CreateAcmeUserAsync(ScenarioAppHostFixture fixture, string email, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        using var http = new HttpClient { BaseAddress = BaseUrl };
        using var tokenResponse = await http.PostAsync(new Uri("realms/master/protocol/openid-connect/token", UriKind.Relative),
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["client_id"] = "admin-cli",
                ["username"] = "admin",
                ["password"] = await AdminPasswordAsync(fixture, ct),
            }),
            ct);
        tokenResponse.EnsureSuccessStatusCode();
        var token = (await tokenResponse.Content.ReadFromJsonAsync<JsonElement>(ct)).GetProperty("access_token").GetString();
        var user = new
        {
            username = email.Split('@')[0],
            email,
            emailVerified = true,
            enabled = true,
            firstName = "Scenario",
            lastName = "Upstream",
            credentials = new[]
            {
                new { type = "password", value = await fixture.ParameterAsync("demo-user-password", ct), temporary = false },
            },
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri($"admin/realms/{AcmeRealm}/users", UriKind.Relative))
        {
            Content = JsonContent.Create(user),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var created = await http.SendAsync(request, ct);
        if (created.StatusCode != HttpStatusCode.Conflict)
        {
            created.EnsureSuccessStatusCode();
        }
    }

    /// <summary>The Keycloak resource's own admin password parameter (not a top-level parameter of the model).</summary>
    private static async Task<string> AdminPasswordAsync(ScenarioAppHostFixture fixture, CancellationToken ct)
    {
        var resource = fixture.App.Services.GetRequiredService<DistributedApplicationModel>().Resources
            .OfType<KeycloakResource>().Single(r => r.Name == "upstream-idp");
        return await resource.AdminPasswordParameter.GetValueAsync(ct)
            ?? throw new InvalidOperationException("upstream-idp has no admin password.");
    }
}
