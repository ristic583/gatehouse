using Gatehouse.Domain.Common;

namespace Gatehouse.Infrastructure.Identity.Keycloak;

/// <summary>Config section <c>Keycloak</c>.</summary>
public sealed class KeycloakOptions
{
    public const string Section = "Keycloak";

    public string AdminUrl { get; set; } = "http://localhost:8080";

    /// <summary>Browser-facing Keycloak URL (broker redirect URIs, SAML SP entity id); defaults to <see cref="AdminUrl"/>.</summary>
    public string? PublicUrl { get; set; }

    public string Realm { get; set; } = "gatehouse";

    public string ProvisionerClientId { get; set; } = "gatehouse-provisioner";

    [Sensitive]
    public string? ProvisionerClientSecret { get; set; }

    public int InviteLifespanSeconds { get; set; } = 72 * 3600;

    /// <summary>Client the invitation returns to after the password is set (with <see cref="InviteRedirectUri"/>).</summary>
    public string InviteClientId { get; set; } = "gatehouse-web";

    /// <summary>
    /// Where the invited admin lands after setting the password (a valid redirect URI of <see cref="InviteClientId"/>, e.g.
    /// <c>{Web:PublicUrl}/auth/callback</c>); unset keeps Keycloak's account page (lab finding #17).
    /// </summary>
    public string? InviteRedirectUri { get; set; }

    /// <summary><c>from=to</c> URL prefix rewrites for IdP endpoints Keycloak calls server-to-server (Development only).</summary>
    public IList<string> BackchannelRewrites { get; } = [];

    public string PublicBase => (PublicUrl ?? AdminUrl).TrimEnd('/');
}
