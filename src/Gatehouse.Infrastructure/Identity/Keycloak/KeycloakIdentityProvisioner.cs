using System.Globalization;
using Gatehouse.Application.Tenancy;
using Gatehouse.Application.Tenancy.IdentityProvisioning;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tenancy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gatehouse.Infrastructure.Identity.Keycloak;

/// <summary>
/// Keycloak 26 admin API adapter (ADR-009 §4). Organization: <c>name</c> = <c>alias</c> = tenant id (unique, server-generated;
/// the display name goes to <c>description</c>). One brokered IdP per tenant, alias <c>{tenantId}-oidc|-saml</c>.
/// Turns refused admin calls into <see cref="FailureCode"/> results; the work is done by <see cref="KeycloakOrganizations"/>,
/// <see cref="KeycloakTenantAdminInvites"/> and <see cref="KeycloakIdentityProviders"/>.
/// </summary>
public sealed class KeycloakIdentityProvisioner : IIdentityProvisioner
{
    public const string FailureCode = "idp.provisioning_failed";

    private readonly ILogger<KeycloakIdentityProvisioner> _logger;
    private readonly KeycloakOrganizations _organizations;
    private readonly KeycloakTenantAdminInvites _invites;
    private readonly KeycloakIdentityProviders _providers;
    private readonly KeycloakProviderConfigs _configs;

    public KeycloakIdentityProvisioner(
        HttpClient http,
        KeycloakAdminTokenCache tokens,
        IOptions<KeycloakOptions> options,
        ILogger<KeycloakIdentityProvisioner> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        var settings = options.Value;
        var client = new KeycloakAdminClient(http, tokens, settings);
        _logger = logger;
        _organizations = new KeycloakOrganizations(client);
        _invites = new KeycloakTenantAdminInvites(client, settings, logger);
        _providers = new KeycloakIdentityProviders(client, logger);
        _configs = new KeycloakProviderConfigs(settings);
    }

    /// <summary><c>execute-actions-email</c> path; with a redirect URI the invited admin returns to Gatehouse instead of the account
    /// console.</summary>
    public static string InviteActionsPath(KeycloakOptions options, string userId)
    {
        ArgumentNullException.ThrowIfNull(options);
        var lifespan = options.InviteLifespanSeconds.ToString(CultureInfo.InvariantCulture);
        var path = $"users/{userId}/execute-actions-email?lifespan={lifespan}";
        if (string.IsNullOrWhiteSpace(options.InviteRedirectUri))
        {
            return path;
        }

        var clientId = Uri.EscapeDataString(options.InviteClientId);
        var redirectUri = Uri.EscapeDataString(options.InviteRedirectUri);
        return $"{path}&client_id={clientId}&redirect_uri={redirectUri}";
    }

    public static string OidcAlias(TenantId tenantId) => $"{tenantId.Value}-oidc";

    public static string SamlAlias(TenantId tenantId) => $"{tenantId.Value}-saml";

    public Task<Result> EnsureOrganizationAsync(TenantId tenantId, string displayName, string emailDomain, CancellationToken ct) =>
        GuardAsync("organization", () => _organizations.EnsureAsync(tenantId, displayName, emailDomain, ct));

    public Task<Result> SetOrganizationEnabledAsync(TenantId tenantId, bool enabled, CancellationToken ct) =>
        GuardAsync("organization", () => _organizations.SetEnabledAsync(tenantId, enabled, ct));

    public Task<Result> EnsureOrganizationMembersAsync(TenantId tenantId, CancellationToken ct) =>
        GuardAsync("organization", () => _organizations.EnsureMembersAsync(tenantId, ct));

    public Task<Result> InviteTenantAdminAsync(
        TenantId tenantId, string email, string givenName, string familyName, CancellationToken ct) =>
        GuardAsync("invite", () => _invites.InviteAsync(tenantId, email, givenName, familyName, ct));

    public async Task<Result<bool>> HasSignedInAsync(TenantId tenantId, string email, CancellationToken ct)
    {
        try
        {
            return await _invites.HasSignedInAsync(tenantId, email, ct);
        }
        catch (Exception ex) when (ex is KeycloakAdminException or HttpRequestException or TaskCanceledException
            && !ct.IsCancellationRequested)
        {
            return Fail(ex, "sign_in_check");
        }
    }

    public async Task<Result<ProvisionedProvider>> UpsertOidcProviderAsync(TenantId tenantId, OidcProviderSpec spec, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var alias = OidcAlias(tenantId);
        var result = await GuardAsync("identity_provider", async () =>
        {
            var provider = new ProviderDefinition(
                alias, "oidc", spec.DisplayName, _configs.Oidc(spec), KeycloakProviderConfigs.OidcMappers(tenantId));
            await _providers.UpsertAsync(tenantId, provider, SamlAlias(tenantId), ct);
            return Result.Success();
        });
        return result.IsFailure ? result.Error : _configs.Provider(alias, IdpKind.Oidc);
    }

    public async Task<Result<ProvisionedProvider>> UpsertSamlProviderAsync(TenantId tenantId, SamlProviderSpec spec, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var alias = SamlAlias(tenantId);
        var result = await GuardAsync("identity_provider", async () =>
        {
            var provider = new ProviderDefinition(
                alias, "saml", spec.DisplayName, _configs.Saml(spec), KeycloakProviderConfigs.SamlMappers(tenantId));
            await _providers.UpsertAsync(tenantId, provider, OidcAlias(tenantId), ct);
            return Result.Success();
        });
        return result.IsFailure ? result.Error : _configs.Provider(alias, IdpKind.Saml);
    }

    public Task<Result> RemoveProviderAsync(TenantId tenantId, CancellationToken ct) =>
        GuardAsync("identity_provider", async () =>
        {
            var removed = await _providers.DeleteAsync(tenantId, OidcAlias(tenantId), ct)
                | await _providers.DeleteAsync(tenantId, SamlAlias(tenantId), ct);
            return removed ? Result.Success() : Error.NotFound("idp.not_configured", "No identity provider is configured.");
        });

    public async Task<Result<ProvisionedProvider?>> GetProviderAsync(TenantId tenantId, CancellationToken ct)
    {
        try
        {
            if (await _providers.ExistsAsync(OidcAlias(tenantId), ct))
            {
                return _configs.Provider(OidcAlias(tenantId), IdpKind.Oidc);
            }

            if (await _providers.ExistsAsync(SamlAlias(tenantId), ct))
            {
                return _configs.Provider(SamlAlias(tenantId), IdpKind.Saml);
            }

            return (ProvisionedProvider?)null;
        }
        catch (Exception ex) when (ex is KeycloakAdminException or HttpRequestException or TaskCanceledException
            && !ct.IsCancellationRequested)
        {
            return Fail(ex, "identity_provider");
        }
    }

    private async Task<Result> GuardAsync(string area, Func<Task<Result>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (ex is KeycloakAdminException or HttpRequestException or TaskCanceledException)
        {
            return Fail(ex, area);
        }
    }

    private Error Fail(Exception ex, string area)
    {
        // Only the operation and status code: request/response bodies may contain client secrets.
        var detail = ex is KeycloakAdminException k ? $"{k.Operation} (HTTP {k.StatusCode})" : ex.GetType().Name;
        if (_logger.IsEnabled(LogLevel.Warning))
        {
            _logger.LogWarning("Keycloak admin call failed: {Area} {Detail}", area, detail);
        }

        return Error.Failure(FailureCode, $"The identity server refused the {area} change: {detail}.");
    }
}
