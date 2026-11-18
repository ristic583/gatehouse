using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Gatehouse.Application.Platform.Dtos;

namespace Gatehouse.IntegrationTests.Scenarios.Infrastructure;

/// <summary>
/// Scenarios that need a tenant of their own never use the demo tenants: the operator creates <c>scn-&lt;stamp&gt;</c>, the invited
/// admin sets a password through the Keycloak form (link from Mailpit) and signs in. Tenants are never deleted (re-runnable; the
/// product has no tenant deletion): remove leftovers with <c>purge-test-tenants</c>, <c>tests/e2e/README.md</c>.
/// </summary>
public static partial class ScenarioTenantFactory
{
    private static readonly TimeSpan ActiveTimeout = TimeSpan.FromMinutes(2);

    public static async Task<ScenarioTenant> CreateWithAdminAsync(ScenarioAppHostFixture fixture, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        var stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)
            + Random.Shared.Next(10, 99).ToString(CultureInfo.InvariantCulture);
        var tenantId = $"scn-{stamp}";
        var domain = $"scn{stamp}.acme.test";
        var adminEmail = $"admin@{domain}";

        using (var platform = await PlatformClient.SignInAsync(fixture, ct))
        {
            using var created = await platform.PostAsync("v1/platform/tenants",
                new CreateTenantRequest(
                    tenantId, $"Scenario {stamp}", domain, adminEmail, "Ada", "Admin", "Europe/Copenhagen"),
                ct);
            if (created.StatusCode != HttpStatusCode.Accepted)
            {
                var body = await created.Content.ReadAsStringAsync(ct);
                throw new InvalidOperationException($"Create tenant {tenantId}: {(int)created.StatusCode} {body}");
            }

            var active = await PacsProbe.WaitUntilAsync(async c =>
                (await platform.GetAsync<JsonElement>($"v1/platform/tenants/{tenantId}", c)).GetProperty("status").GetString() == "Active",
                ActiveTimeout, ct);
            if (!active)
            {
                throw new TimeoutException($"Tenant {tenantId} did not become Active within {ActiveTimeout}.");
            }
        }

        var password = $"Scn-{stamp}-Pw!";
        await SetPasswordAsync(await MailpitProbe.ActionLinkForAsync(adminEmail, TimeSpan.FromSeconds(60), ct), password, ct);
        var admin = new ScenarioClient(fixture.WebUrl, adminEmail, password);
        try
        {
            await admin.SignInAsync(ct);
            return new ScenarioTenant(tenantId, domain, adminEmail, admin);
        }
        catch
        {
            admin.Dispose();
            throw;
        }
    }

    /// <summary>Keycloak <c>UPDATE_PASSWORD</c> action: optional "proceed" page, then the new-password form.</summary>
    private static async Task SetPasswordAsync(Uri actionLink, string password, CancellationToken ct)
    {
#pragma warning disable CA2000 // The handlers are owned and disposed by the HttpClient.
        using var http = new HttpClient(
            new LocalhostCookieHandler(new CookieContainer(), new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false }),
            disposeHandler: true);
#pragma warning restore CA2000
        var page = actionLink;
        var html = await http.GetStringAsync(page, ct);
        if (ProceedLink().Match(html) is { Success: true } proceed)
        {
            page = new Uri(page, WebUtility.HtmlDecode(proceed.Groups["href"].Value));
            html = await http.GetStringAsync(page, ct);
        }

        var action = FormAction().Match(html);
        if (!action.Success || !html.Contains("password-new", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"The invitation link did not open the Keycloak password form: {Title(html)}");
        }

        using var form = new FormUrlEncodedContent(
            new Dictionary<string, string> { ["password-new"] = password, ["password-confirm"] = password });
        using var response = await http.PostAsync(new Uri(page, WebUtility.HtmlDecode(action.Groups["action"].Value)), form, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (body.Contains("password-new", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Setting the admin password failed ({(int)response.StatusCode}): {Title(body)}");
        }
    }

    private static string Title(string html)
    {
        var feedback = FeedbackText().Match(html);
        return feedback.Success ? WebUtility.HtmlDecode(feedback.Groups["text"].Value.Trim()) : "(no message)";
    }

    [GeneratedRegex("(?:kc-feedback-text|instruction|pf-v5-c-alert__title)[^>]*>(?<text>[^<]+)<", RegexOptions.IgnoreCase)]
    private static partial Regex FeedbackText();

    [GeneratedRegex("<a\\b[^>]*href=\"(?<href>[^\"]+)\"[^>]*>[^<]*proceed", RegexOptions.IgnoreCase)]
    private static partial Regex ProceedLink();

    [GeneratedRegex("<form\\b[^>]*\\baction=\"(?<action>[^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex FormAction();
}
