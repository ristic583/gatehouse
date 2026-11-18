using Gatehouse.Domain.Tenancy;

namespace Gatehouse.Application.Identity.IdentityProviders;

public sealed record IdentityProviderDto(
    IdpKind Kind,
    string Alias,
    string DisplayName,
    string? DiscoveryUrl,
    string? Issuer,
    string? ClientId,
    bool HasClientSecret,
    string? SamlEntityId,
    string? SamlSsoUrl,
    string? SigningCertificateThumbprint,
    string EmailDomain,
    string BrokerRedirectUri,
    string? SpEntityId,
    DateTimeOffset SavedAt,
    DateTimeOffset? LastTestedAt,
    bool? LastTestOk);
