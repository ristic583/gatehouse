using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Gatehouse.Infrastructure.Identity.Keycloak;

/// <summary>Singleton: client-credentials token of <c>gatehouse-provisioner</c>, reused until 30 s before expiry.</summary>
public sealed class KeycloakAdminTokenCache(IOptions<KeycloakOptions> options, TimeProvider time) : IDisposable
{
    private static readonly TimeSpan Margin = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _lock = new(1, 1);
    private string? _token;
    private DateTimeOffset _expiresAt;

    public async Task<string> GetAsync(HttpClient http, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(http);
        if (_token is { } cached && time.GetUtcNow() < _expiresAt)
        {
            return cached;
        }

        await _lock.WaitAsync(ct);
        try
        {
            if (_token is { } again && time.GetUtcNow() < _expiresAt)
            {
                return again;
            }

            var o = options.Value;
            using var request = new HttpRequestMessage(
                HttpMethod.Post, new Uri($"{o.AdminUrl.TrimEnd('/')}/realms/{o.Realm}/protocol/openid-connect/token"))
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = o.ProvisionerClientId,
                    ["client_secret"] = o.ProvisionerClientSecret ?? string.Empty,
                }),
            };
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                throw new KeycloakAdminException("token", (int)response.StatusCode);
            }

            var body = await response.Content.ReadFromJsonAsync<TokenResponse>(ct)
                ?? throw new KeycloakAdminException("token", (int)response.StatusCode);
            _token = body.AccessToken;
            _expiresAt = time.GetUtcNow().AddSeconds(body.ExpiresIn) - Margin;
            return _token;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose() => _lock.Dispose();

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
