namespace Gatehouse.Application.Identity.IdentityProviders;

/// <summary>Exactly one of the two is set.</summary>
public sealed record SamlSettingsRequest(string? MetadataUrl, string? MetadataXml);
