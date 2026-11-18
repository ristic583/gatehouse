using Gatehouse.Domain.Common;

namespace Gatehouse.Application.Tenancy.IdentityProvisioning;

/// <summary>Resolved OIDC endpoints (from discovery) for the tenant's upstream IdP. <c>ClientSecret</c> null = keep the stored
/// one.</summary>
public sealed record OidcProviderSpec(
    string DisplayName,
    string Issuer,
    string AuthorizationUrl,
    string TokenUrl,
    string JwksUrl,
    string? UserInfoUrl,
    string ClientId,
    [property: Sensitive] string? ClientSecret,
    IReadOnlyList<string> Scopes);
