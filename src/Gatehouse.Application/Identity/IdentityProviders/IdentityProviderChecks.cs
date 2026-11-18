using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tenancy;

namespace Gatehouse.Application.Identity.IdentityProviders;

/// <summary>Server-side metadata checks shared by test and save. <c>idp.url_not_allowed</c> is returned as an error, other problems as
/// failed checks.</summary>
public sealed class IdentityProviderChecks(IIdpMetadataFetcher fetcher)
{
    public const string MetadataReachable = "metadata_reachable";
    public const string IssuerMatches = "issuer_matches";
    public const string SigningKeysPresent = "signing_keys_present";
    public const string SsoEndpointPresent = "sso_endpoint_present";
    private const string WellKnown = "/.well-known/openid-configuration";

    public async Task<Result<IdpCheckOutcome>> RunAsync(SaveIdentityProviderRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Kind == IdpKind.Saml ? await SamlAsync(request.Saml!, ct) : await OidcAsync(request.Oidc!, ct);
    }

    private async Task<Result<IdpCheckOutcome>> OidcAsync(OidcSettingsRequest oidc, CancellationToken ct)
    {
        var fetched = await fetcher.FetchOidcDiscoveryAsync(oidc.DiscoveryUrl.Trim(), ct);
        if (fetched.IsFailure && fetched.Error.Code == "idp.url_not_allowed")
        {
            return fetched.Error;
        }

        if (fetched.IsFailure)
        {
            return Outcome(null, null,
                new(MetadataReachable, false, fetched.Error.Code, fetched.Error.Message),
                new(IssuerMatches, false, null, null),
                new(SigningKeysPresent, false, null, null));
        }

        var discovery = fetched.Value;
        var url = oidc.DiscoveryUrl.Trim();
        var expected = url.EndsWith(WellKnown, StringComparison.Ordinal) ? url[..^WellKnown.Length] : url;
        var issuerOk = string.Equals(discovery.Issuer.TrimEnd('/'), expected.TrimEnd('/'), StringComparison.Ordinal);
        return Outcome(discovery, null,
            new(MetadataReachable, true, null, null),
            new(
                IssuerMatches, issuerOk, issuerOk ? null : "idp.metadata_invalid",
                issuerOk ? null : $"Issuer '{discovery.Issuer}' does not match the discovery URL."),
            new(
                SigningKeysPresent, discovery.SigningKeys > 0, discovery.SigningKeys > 0 ? null : "idp.metadata_invalid",
                discovery.SigningKeys > 0 ? null : "The JWKS has no signing key."));
    }

    private async Task<Result<IdpCheckOutcome>> SamlAsync(SamlSettingsRequest saml, CancellationToken ct)
    {
        var parsed = await fetcher.ParseSamlMetadataAsync(saml.MetadataUrl?.Trim(), saml.MetadataXml, ct);
        if (parsed.IsFailure && parsed.Error.Code == "idp.url_not_allowed")
        {
            return parsed.Error;
        }

        if (parsed.IsFailure)
        {
            return Outcome(null, null,
                new(MetadataReachable, false, parsed.Error.Code, parsed.Error.Message),
                new(SsoEndpointPresent, false, null, null),
                new(SigningKeysPresent, false, null, null));
        }

        var m = parsed.Value;
        return Outcome(null, m,
            new(MetadataReachable, true, null, null),
            new(
                SsoEndpointPresent, m.SsoUrl is not null, m.SsoUrl is null ? "idp.metadata_invalid" : null,
                m.SsoUrl is null ? "No HTTP-POST SingleSignOnService." : null),
            new(
                SigningKeysPresent, m.SigningCertificatePem is not null, m.SigningCertificatePem is null ? "idp.metadata_invalid" : null,
                m.SigningCertificatePem is null ? "No signing certificate." : null));
    }

    private static IdpCheckOutcome Outcome(OidcDiscovery? oidc, SamlMetadata? saml, params IdpTestCheckDto[] checks) =>
        new(new IdpTestResultDto(checks.All(c => c.Ok), checks), oidc, saml);
}
