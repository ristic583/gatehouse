using System.Text.Json.Nodes;

namespace Gatehouse.Infrastructure.Identity.Keycloak;

/// <summary>An identity provider instance to save: Keycloak <c>providerId</c> (<c>oidc</c>/<c>saml</c>), config and mappers.</summary>
internal sealed record ProviderDefinition(
    string Alias,
    string ProviderId,
    string DisplayName,
    JsonObject Config,
    List<JsonObject> Mappers);
