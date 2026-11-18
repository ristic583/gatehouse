using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tenancy;

namespace Gatehouse.Infrastructure.Identity.Keycloak;

/// <summary>
/// Authenticated calls to the Keycloak admin API of the configured realm, plus the organization and user lookups every
/// provisioning step needs. Refused calls throw <see cref="KeycloakAdminException"/> with the operation and status only.
/// </summary>
internal sealed class KeycloakAdminClient(HttpClient http, KeycloakAdminTokenCache tokens, KeycloakOptions options)
{
    public const string TenantAttribute = "tenant_id";

    private string AdminBase => $"{options.AdminUrl.TrimEnd('/')}/admin/realms/{options.Realm}";

    public async Task<JsonNode?> FindOrganizationAsync(TenantId tenantId, CancellationToken ct)
    {
        var search = Uri.EscapeDataString(tenantId.Value);
        var list = await GetJsonAsync($"organizations?search={search}&exact=true&briefRepresentation=false", "organization.find", ct);
        return list?.AsArray().FirstOrDefault(o => (string?)o?["alias"] == tenantId.Value);
    }

    /// <summary>An organization found by its verified domain counts only when its alias is the tenant id (never adopts another
    /// tenant's).</summary>
    public async Task<JsonNode?> FindOrganizationByDomainAsync(TenantId tenantId, string domain, CancellationToken ct)
    {
        var path = $"organizations?search={Uri.EscapeDataString(domain)}&exact=true&briefRepresentation=false";
        var list = await GetJsonAsync(path, "organization.find", ct);
        return list?.AsArray().FirstOrDefault(o => (string?)o?["alias"] == tenantId.Value);
    }

    public async Task<JsonNode?> FindUserAsync(string email, CancellationToken ct)
    {
        var list = await GetJsonAsync($"users?email={Uri.EscapeDataString(email)}&exact=true&briefRepresentation=false", "user.find", ct);
        return list?.AsArray().FirstOrDefault(u => string.Equals((string?)u?["email"], email, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Adds a user to an organization; an existing membership (409) counts as done.</summary>
    public async Task AddOrganizationMemberAsync(string? organizationId, string? userId, CancellationToken ct)
    {
        using var member = await SendRawAsync(HttpMethod.Post, $"organizations/{organizationId}/members", JsonValue.Create(userId), ct);
        if (member.StatusCode != HttpStatusCode.Conflict)
        {
            Ensure(member, "organization.member");
        }
    }

    public async Task<JsonNode?> GetJsonAsync(string path, string operation, CancellationToken ct)
    {
        using var response = await SendRawAsync(HttpMethod.Get, path, null, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        Ensure(response, operation);
        return await response.Content.ReadFromJsonAsync<JsonNode>(ct);
    }

    public Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, JsonNode body, CancellationToken ct) =>
        SendRawAsync(method, path, body, ct);

    public async Task<HttpResponseMessage> SendRawAsync(HttpMethod method, string path, JsonNode? body, CancellationToken ct)
    {
        var token = await tokens.GetAsync(http, ct);
        using var request = new HttpRequestMessage(method, new Uri($"{AdminBase}/{path}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await http.SendAsync(request, ct);
    }

    public static void Ensure(HttpResponseMessage response, string operation)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new KeycloakAdminException(operation, (int)response.StatusCode);
        }
    }
}
