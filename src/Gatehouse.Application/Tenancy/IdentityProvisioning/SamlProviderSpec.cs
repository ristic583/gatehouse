namespace Gatehouse.Application.Tenancy.IdentityProvisioning;

public sealed record SamlProviderSpec(string DisplayName, string EntityId, string SsoUrl, string SigningCertificatePem);
