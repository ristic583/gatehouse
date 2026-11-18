using System.Security.Claims;
using System.Text.Encodings.Web;
using Gatehouse.Application.Platform;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gatehouse.IntegrationTests.Infrastructure;

/// <summary>
/// Test-only stand-in for the <c>PlatformBearer</c> scheme: claims come from <c>X-Test-Platform-Sub</c> and
/// <c>X-Test-Platform-Email</c>. It authenticates the platform scheme only, never the tenant default scheme.
/// </summary>
public sealed class TestPlatformAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SubHeader = "X-Test-Platform-Sub";
    public const string EmailHeader = "X-Test-Platform-Email";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var sub = Request.Headers[SubHeader].ToString();
        if (string.IsNullOrEmpty(sub))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim> { new("sub", sub) };
        var email = Request.Headers[EmailHeader].ToString();
        if (!string.IsNullOrEmpty(email))
        {
            claims.Add(new Claim("email", email));
        }

        var identity = new ClaimsIdentity(claims, PlatformAuthentication.Scheme, "email", "role");
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), PlatformAuthentication.Scheme)));
    }
}
