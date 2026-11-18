using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gatehouse.IntegrationTests.Infrastructure;

/// <summary>
/// Test-only authentication: claims come from <c>X-Test-Sub</c>, <c>X-Test-Email</c> and <c>X-Test-Tenant</c>.
/// No <c>X-Test-Sub</c> means anonymous. Registered only by <see cref="GatehouseApiFactory"/>.
/// </summary>
public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "TestAuth";
    public const string SubHeader = "X-Test-Sub";
    public const string EmailHeader = "X-Test-Email";
    public const string TenantHeader = "X-Test-Tenant";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var sub = Request.Headers[SubHeader].ToString();
        if (string.IsNullOrEmpty(sub))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim> { new("sub", sub) };
        AddIfPresent(claims, EmailHeader, "email");
        AddIfPresent(claims, TenantHeader, "tenant_id");

        var identity = new ClaimsIdentity(claims, SchemeName, "email", "role");
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    private void AddIfPresent(List<Claim> claims, string header, string claimType)
    {
        var value = Request.Headers[header].ToString();
        if (!string.IsNullOrEmpty(value))
        {
            claims.Add(new Claim(claimType, value));
        }
    }
}
