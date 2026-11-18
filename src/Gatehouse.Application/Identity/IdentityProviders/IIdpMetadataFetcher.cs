using Gatehouse.Domain.Common;

namespace Gatehouse.Application.Identity.IdentityProviders;

/// <summary>
/// Fetches tenant-supplied metadata under ADR-009 §4 SSRF rules. Errors: <c>idp.url_not_allowed</c> (scheme, private address,
/// redirect), <c>idp.unreachable</c>, <c>idp.metadata_invalid</c> (unparsable or larger than 1 MiB).
/// </summary>
public interface IIdpMetadataFetcher
{
    public Task<Result<OidcDiscovery>> FetchOidcDiscoveryAsync(string discoveryUrl, CancellationToken ct);

    public Task<Result<SamlMetadata>> ParseSamlMetadataAsync(string? metadataUrl, string? metadataXml, CancellationToken ct);
}
