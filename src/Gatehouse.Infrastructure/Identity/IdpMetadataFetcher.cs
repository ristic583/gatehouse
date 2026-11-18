using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Gatehouse.Application.Identity.IdentityProviders;
using Gatehouse.Domain.Common;
using Microsoft.Extensions.Options;

namespace Gatehouse.Infrastructure.Identity;

/// <summary>
/// ADR-009 §4: HTTPS only, DNS resolved first and private/loopback/link-local/multicast addresses refused (the production
/// handler re-checks at connect time, see <see cref="IdpAddressRules.CreateHandler"/>), no redirects, 5 s timeout, 1 MiB limit,
/// SAML XML parsed with DTDs prohibited and no resolver. Hosts in <c>Identity:AllowedInsecureHosts</c> skip the scheme
/// and address rules (Development stubs).
/// </summary>
public sealed class IdpMetadataFetcher : IIdpMetadataFetcher
{
    public const int MaxBytes = 1024 * 1024;
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly HttpClient _http;
    private readonly IdentityProviderOptions _options;
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> _resolve;

    public IdpMetadataFetcher(HttpClient http, IOptions<IdentityProviderOptions> options)
        : this(http, options, (host, ct) => Dns.GetHostAddressesAsync(host, ct))
    {
    }

    public IdpMetadataFetcher(
        HttpClient http, IOptions<IdentityProviderOptions> options, Func<string, CancellationToken, Task<IPAddress[]>> resolve)
    {
        ArgumentNullException.ThrowIfNull(options);
        _http = http;
        _options = options.Value;
        _resolve = resolve;
    }

    /// <inheritdoc cref="IdpAddressRules.CreateHandler"/>
    public static SocketsHttpHandler CreateHandler(IdentityProviderOptions options) => IdpAddressRules.CreateHandler(options);

    /// <inheritdoc cref="IdpAddressRules.IsPublic"/>
    public static bool IsPublic(IPAddress address) => IdpAddressRules.IsPublic(address);

    public async Task<Result<OidcDiscovery>> FetchOidcDiscoveryAsync(string discoveryUrl, CancellationToken ct)
    {
        var body = await GetAsync(discoveryUrl, ct);
        if (body.IsFailure)
        {
            return body.Error;
        }

        string issuer, authorization, token, jwks;
        string? userInfo;
        try
        {
            using var doc = JsonDocument.Parse(body.Value);
            var root = doc.RootElement;
            issuer = Required(root, "issuer");
            authorization = Required(root, "authorization_endpoint");
            token = Required(root, "token_endpoint");
            jwks = Required(root, "jwks_uri");
            userInfo = root.TryGetProperty("userinfo_endpoint", out var ui) && ui.ValueKind == JsonValueKind.String ? ui.GetString() : null;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return Invalid("The discovery document is not valid OpenID Connect metadata.");
        }

        // Keycloak calls token and userinfo server-to-server: they get the same destination rules as discovery and JWKS (GH-ADMIN-01).
        foreach (var endpoint in new[] { authorization, token, userInfo })
        {
            if (endpoint is not null && await CheckUrlAsync(endpoint, ct) is { } refused)
            {
                return refused;
            }
        }

        var keys = await GetAsync(jwks, ct);
        if (keys.IsFailure)
        {
            return keys.Error;
        }

        int signing;
        try
        {
            using var doc = JsonDocument.Parse(keys.Value);
            signing = doc.RootElement.TryGetProperty("keys", out var arr) && arr.ValueKind == JsonValueKind.Array
                ? arr.EnumerateArray().Count(k => k.TryGetProperty("kty", out _)
                    && (!k.TryGetProperty("use", out var use) || use.GetString() == "sig"))
                : 0;
        }
        catch (JsonException)
        {
            return Invalid("The JWKS is not valid JSON.");
        }

        return new OidcDiscovery(issuer, authorization, token, jwks, userInfo, signing);
    }

    public async Task<Result<SamlMetadata>> ParseSamlMetadataAsync(string? metadataUrl, string? metadataXml, CancellationToken ct)
    {
        string xml;
        if (!string.IsNullOrWhiteSpace(metadataUrl))
        {
            var body = await GetAsync(metadataUrl, ct);
            if (body.IsFailure)
            {
                return body.Error;
            }

            xml = body.Value;
        }
        else if (metadataXml is { Length: > 0 and <= MaxBytes })
        {
            xml = metadataXml;
        }
        else
        {
            return Invalid("SAML metadata is missing or larger than 1 MiB.");
        }

        return SamlMetadataParser.Parse(xml);
    }

    /// <summary>Scheme and resolved-address rules for any URL taken from metadata; null when allowed.</summary>
    private async Task<Error?> CheckUrlAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return NotAllowed("The URL must be an absolute HTTPS URL.");
        }

        if (IdpAddressRules.IsAllowedInsecure(_options, uri.Host, uri.Port))
        {
            return null;
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            return NotAllowed("Only HTTPS URLs are allowed.");
        }

        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(uri.Host, out var literal) ? [literal] : await _resolve(uri.DnsSafeHost, ct);
        }
        catch (SocketException)
        {
            return Error.Failure("idp.unreachable", "The host name cannot be resolved.");
        }

        return addresses.Length == 0 || !addresses.All(IdpAddressRules.IsPublic)
            ? NotAllowed("The URL resolves to a private, loopback or link-local address.")
            : null;
    }

    private async Task<Result<string>> GetAsync(string url, CancellationToken ct)
    {
        if (await CheckUrlAsync(url, ct) is { } refused)
        {
            return refused;
        }

        var uri = new Uri(url, UriKind.Absolute);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var status = (int)response.StatusCode;
            if (status is >= 300 and < 400)
            {
                return NotAllowed("Redirects are not followed.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return Error.Failure("idp.unreachable", $"The server answered HTTP {status}.");
            }

            if (response.Content.Headers.ContentLength > MaxBytes)
            {
                return Invalid("Metadata is larger than 1 MiB.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await stream.ReadAsync(chunk, timeout.Token)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > MaxBytes)
                {
                    return Invalid("Metadata is larger than 1 MiB.");
                }
            }

            return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Error.Failure("idp.unreachable", "The server did not answer within 5 seconds.");
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("idp.url_not_allowed", StringComparison.Ordinal))
        {
            return NotAllowed("The URL resolves to a private, loopback or link-local address.");
        }
        catch (HttpRequestException)
        {
            return Error.Failure("idp.unreachable", "The server cannot be reached.");
        }
    }

    private static string Required(JsonElement root, string name) =>
        root.GetProperty(name).GetString() is { Length: > 0 } value ? value : throw new KeyNotFoundException(name);

    private static Error NotAllowed(string message) => Error.Validation("idp.url_not_allowed", message);

    private static Error Invalid(string message) => Error.Validation("idp.metadata_invalid", message);
}
