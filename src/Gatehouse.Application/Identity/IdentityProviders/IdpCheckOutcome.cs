namespace Gatehouse.Application.Identity.IdentityProviders;

public sealed record IdpCheckOutcome(IdpTestResultDto Result, OidcDiscovery? Oidc, SamlMetadata? Saml);
