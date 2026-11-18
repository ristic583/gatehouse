namespace Gatehouse.Application.Identity.IdentityProviders;

public sealed record SamlMetadata(string EntityId, string? SsoUrl, string? SigningCertificatePem, string? SigningCertificateThumbprint);
