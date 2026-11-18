using System.Net;

namespace Gatehouse.IntegrationTests.Scenarios.Infrastructure;

/// <summary>
/// Cookies and redirects like a browser on <c>http://localhost</c>: browsers treat localhost as a secure context and send
/// <c>Secure</c> cookies (Keycloak's <c>AUTH_SESSION_ID</c>, <c>KC_RESTART</c>) over http, <see cref="CookieContainer"/> does not.
/// Cookies are stored and read under the https form of each URL, and redirects are followed here so every hop's
/// <c>Set-Cookie</c> is kept.
/// </summary>
internal sealed class LocalhostCookieHandler(CookieContainer cookies, HttpMessageHandler inner) : DelegatingHandler(inner)
{
    private const int MaxRedirects = 10;

    public static Uri CookieUri(Uri uri) => new UriBuilder(uri) { Scheme = Uri.UriSchemeHttps, Port = uri.Port }.Uri;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var current = request;
        for (var hop = 0; ; hop++)
        {
            var header = cookies.GetCookieHeader(CookieUri(current.RequestUri!));
            current.Headers.Remove("Cookie");
            if (header.Length > 0)
            {
                current.Headers.Add("Cookie", header);
            }

            var response = await base.SendAsync(current, cancellationToken);
            if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
            {
                foreach (var setCookie in setCookies)
                {
                    cookies.SetCookies(CookieUri(current.RequestUri!), setCookie);
                }
            }

            var status = (int)response.StatusCode;
            if (status is < 300 or >= 400 || response.Headers.Location is null || hop >= MaxRedirects)
            {
                return response;
            }

            var next = new Uri(current.RequestUri!, response.Headers.Location);
            var keepMethod = status is 307 or 308;
            response.Dispose();
            var redirect = new HttpRequestMessage(keepMethod ? current.Method : HttpMethod.Get, next);
            if (keepMethod)
            {
                redirect.Content = current.Content;
            }

            current = redirect;
        }
    }
}
