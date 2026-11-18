using Gatehouse.Domain.Common;

namespace Gatehouse.Application.Identity.IdentityProviders;

/// <summary><c>ClientSecret</c> is required on create; <c>null</c> on update keeps the secret stored in Keycloak.</summary>
public sealed record OidcSettingsRequest(
    string DiscoveryUrl, string ClientId, [property: Sensitive] string? ClientSecret, IReadOnlyList<string>? Scopes);
