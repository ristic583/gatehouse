using Gatehouse.Domain.Tenancy;

namespace Gatehouse.Application.Tenancy.IdentityProvisioning;

/// <summary>The tenant's single brokered IdP. <c>SpEntityId</c> is set for SAML only.</summary>
public sealed record ProvisionedProvider(string Alias, IdpKind Kind, string BrokerRedirectUri, string? SpEntityId);
