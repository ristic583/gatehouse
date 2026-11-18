using Gatehouse.Domain.Tenancy;

namespace Gatehouse.Application.Identity.IdentityProviders;

/// <summary>Summary in <c>tenant_settings</c> key <c>identity.provider</c> (<c>domain-model.md</c> §2.10). No secrets.</summary>
public sealed record TenantIdentityProvider(
    IdpKind Kind,
    string Alias,
    string DisplayName,
    string? DiscoveryUrl,
    string? Issuer,
    string? ClientId,
    string? SamlEntityId,
    string? SamlSsoUrl,
    string? SigningCertificateThumbprint,
    DateTimeOffset SavedAt,
    DateTimeOffset? LastTestedAt,
    bool? LastTestOk);
