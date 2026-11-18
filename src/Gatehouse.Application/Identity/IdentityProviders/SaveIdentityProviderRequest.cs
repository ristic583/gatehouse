using Gatehouse.Domain.Tenancy;

namespace Gatehouse.Application.Identity.IdentityProviders;

public sealed record SaveIdentityProviderRequest(IdpKind Kind, string DisplayName, OidcSettingsRequest? Oidc, SamlSettingsRequest? Saml);
