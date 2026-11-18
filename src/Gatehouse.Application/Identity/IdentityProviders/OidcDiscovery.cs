namespace Gatehouse.Application.Identity.IdentityProviders;

public sealed record OidcDiscovery(
    string Issuer, string AuthorizationUrl, string TokenUrl, string JwksUrl, string? UserInfoUrl, int SigningKeys);
