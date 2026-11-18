using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;

namespace Gatehouse.IntegrationTests.Scenarios.Infrastructure;

/// <summary>
/// A browser-like session of one demo user against the web BFF: signs in through the email screen, Keycloak and the upstream IdP
/// (no direct-access grants), keeps <c>gh_session</c> and calls <c>/api/proxy/v1/...</c> with the <c>Origin</c> header.
/// </summary>
public sealed partial class ScenarioClient : IDisposable
{
    private const string UpstreamIdpPort = "8081";
    private readonly HttpClient _http;
    private readonly CookieContainer _cookies = new();
    private readonly Uri _web;
    private readonly string _password;

    public ScenarioClient(Uri web, string email, string password)
    {
        _web = web;
        _password = password;
        Email = email;
#pragma warning disable CA2000 // The handlers are owned and disposed by the HttpClient.
        var handler = new LocalhostCookieHandler(_cookies, new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false });
#pragma warning restore CA2000
        _http = new HttpClient(handler, disposeHandler: true) { BaseAddress = web, Timeout = TimeSpan.FromMinutes(2) };
    }

    public string Email { get; }

    /// <summary>The web BFF origin this session belongs to.</summary>
    public Uri Web => _web;

    /// <summary>A handler that sends this session's cookies (for the SignalR client of <see cref="LiveHubProbe"/>).</summary>
    public HttpMessageHandler CreateCookieHandler() =>
        new LocalhostCookieHandler(_cookies, new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false });

    public bool SignedIn => HasCookie("gh_session");

    /// <summary>Email-first POST, then the Keycloak/upstream login forms, until the callback set <c>gh_session</c>.</summary>
    public Task SignInAsync(CancellationToken ct) =>
        SignInAsync("/auth/login", new Dictionary<string, string> { ["email"] = Email, ["returnTo"] = "/" }, "gh_session", otpSecret: null, ct);

    /// <summary>Operator sign-in: <c>/platform/auth/login</c>, then password and TOTP forms of realm <c>gatehouse-platform</c>.</summary>
    public Task SignInPlatformAsync(string otpSecret, CancellationToken ct) =>
        SignInAsync("/platform/auth/login", new Dictionary<string, string> { ["returnTo"] = "/platform" }, "gh_platform_session", otpSecret, ct);

    private bool HasCookie(string name) => _cookies.GetCookies(LocalhostCookieHandler.CookieUri(_web)).Any(c => c.Name == name);

    private async Task SignInAsync(string startPath, Dictionary<string, string> startForm, string sessionCookie, string? otpSecret, CancellationToken ct)
    {
        using var start = new HttpRequestMessage(HttpMethod.Post, new Uri(startPath, UriKind.Relative)) { Content = new FormUrlEncodedContent(startForm) };
        start.Headers.Add("Origin", _web.GetLeftPart(UriPartial.Authority));
        var response = await _http.SendAsync(start, ct);
        try
        {
            for (var step = 0; step < 5 && !HasCookie(sessionCookie); step++)
            {
                var page = response.RequestMessage?.RequestUri ?? _web;
                var html = await response.Content.ReadAsStringAsync(ct);
                var action = LoginFormAction(html)
                    ?? throw new InvalidOperationException($"Sign-in of {Email} stopped at {page.GetLeftPart(UriPartial.Path)} ({(int)response.StatusCode}).");
                Dictionary<string, string> fields;
                if (otpSecret is not null && html.Contains("name=\"otp\"", StringComparison.Ordinal))
                {
                    fields = new Dictionary<string, string> { ["otp"] = await Totp.FreshAsync(otpSecret, ct) };
                }
                else
                {
                    var username = page.Port.ToString(System.Globalization.CultureInfo.InvariantCulture) == UpstreamIdpPort ? Email.Split('@')[0] : Email;
                    fields = new Dictionary<string, string> { ["username"] = username, ["password"] = _password, ["credentialId"] = string.Empty };
                }

                response.Dispose();
                using var form = new FormUrlEncodedContent(fields);
                response = await _http.PostAsync(new Uri(page, action), form, ct);
            }
        }
        finally
        {
            response.Dispose();
        }

        if (!HasCookie(sessionCookie))
        {
            throw new InvalidOperationException($"Sign-in of {Email} did not produce a {sessionCookie} cookie.");
        }
    }

    public async Task<T> GetAsync<T>(string path, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Get, path, null, ct);
        return await ReadAsync<T>(response, ct);
    }

    public async Task<T> PostAsync<T>(string path, object? body, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Post, path, body, ct);
        return await ReadAsync<T>(response, ct);
    }

    public async Task<T> PutAsync<T>(string path, object? body, CancellationToken ct)
    {
        using var response = await SendAsync(HttpMethod.Put, path, body, ct);
        return await ReadAsync<T>(response, ct);
    }

    /// <summary>Raw call for tests that assert a status code; the caller disposes the response.</summary>
    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, ProxyUri(path));
        if (method != HttpMethod.Get)
        {
            request.Headers.Add("Origin", _web.GetLeftPart(UriPartial.Authority));
        }

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: ScenarioJson.Options);
        }

        return await _http.SendAsync(request, ct);
    }

    public Task<HttpResponseMessage> PostAsync(string path, object? body, CancellationToken ct) => SendAsync(HttpMethod.Post, path, body, ct);

    public Task<HttpResponseMessage> PutAsync(string path, object? body, CancellationToken ct) => SendAsync(HttpMethod.Put, path, body, ct);

    public Task<HttpResponseMessage> DeleteAsync(string path, CancellationToken ct) => SendAsync(HttpMethod.Delete, path, null, ct);

    public void Dispose() => _http.Dispose();

    /// <summary>Accepts <c>v1/...</c>, <c>/api/v1/...</c> or <c>/api/proxy/v1/...</c>.</summary>
    private static Uri ProxyUri(string path)
    {
        var trimmed = path.TrimStart('/');
        trimmed = trimmed.StartsWith("api/proxy/", StringComparison.Ordinal) ? trimmed["api/proxy/".Length..]
            : trimmed.StartsWith("api/", StringComparison.Ordinal) ? trimmed["api/".Length..]
            : trimmed;
        return new Uri("/api/proxy/" + trimmed, UriKind.Relative);
    }

    private async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.PathAndQuery} as {Email}: {(int)response.StatusCode} {text}");
        }

        return (await response.Content.ReadFromJsonAsync<T>(ScenarioJson.Options, ct))!;
    }

    private static string? LoginFormAction(string html)
    {
        var forms = FormTag().Matches(html);
        var form = forms.FirstOrDefault(f => f.Value.Contains("kc-form-login", StringComparison.Ordinal)) ?? forms.FirstOrDefault();
        return form is null ? null : WebUtility.HtmlDecode(form.Groups["action"].Value);
    }

    [GeneratedRegex("<form\\b[^>]*\\baction=\"(?<action>[^\"]+)\"[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex FormTag();
}
