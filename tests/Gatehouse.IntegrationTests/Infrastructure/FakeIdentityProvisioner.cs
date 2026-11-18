using System.Collections.Concurrent;
using Gatehouse.Application.Tenancy;
using Gatehouse.Application.Tenancy.IdentityProvisioning;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tenancy;

namespace Gatehouse.IntegrationTests.Infrastructure;

/// <summary>In-memory <see cref="IIdentityProvisioner"/>: records calls; <see cref="FailNext"/> makes a method fail once.</summary>
public sealed class FakeIdentityProvisioner : IIdentityProvisioner
{
    private readonly ConcurrentDictionary<string, string> _failures = new(StringComparer.Ordinal);

    public ConcurrentQueue<string> Calls { get; } = new();

    public ConcurrentDictionary<TenantId, (string Domain, bool Enabled)> Organizations { get; } = new();

    public ConcurrentDictionary<TenantId, ProvisionedProvider> Providers { get; } = new();

    public ConcurrentDictionary<(TenantId, string), bool> Invites { get; } = new();

    /// <summary>Emails (per tenant) that count as signed in.</summary>
    public ConcurrentDictionary<(TenantId, string), bool> SignedIn { get; } = new();

    public void FailNext(string method, string code = "idp.provisioning_failed") => _failures[method] = code;

    public Task<Result> EnsureOrganizationAsync(TenantId tenantId, string displayName, string emailDomain, CancellationToken ct)
    {
        if (Record(nameof(EnsureOrganizationAsync), tenantId) is { } error)
        {
            return Task.FromResult<Result>(error);
        }

        if (Organizations.Any(o => o.Key != tenantId && o.Value.Domain == emailDomain))
        {
            return Task.FromResult<Result>(Error.Conflict("tenant.email_domain_taken", "Domain taken."));
        }

        Organizations[tenantId] = (emailDomain, true);
        return Task.FromResult(Result.Success());
    }

    public Task<Result> SetOrganizationEnabledAsync(TenantId tenantId, bool enabled, CancellationToken ct)
    {
        if (Record(nameof(SetOrganizationEnabledAsync), tenantId) is { } error)
        {
            return Task.FromResult<Result>(error);
        }

        if (Organizations.TryGetValue(tenantId, out var org))
        {
            Organizations[tenantId] = (org.Domain, enabled);
        }

        return Task.FromResult(Result.Success());
    }

    public Task<Result> EnsureOrganizationMembersAsync(TenantId tenantId, CancellationToken ct) =>
        Task.FromResult(Record(nameof(EnsureOrganizationMembersAsync), tenantId) is { } error ? (Result)error : Result.Success());

    public Task<Result> InviteTenantAdminAsync(TenantId tenantId, string email, string givenName, string familyName, CancellationToken ct)
    {
        if (Record(nameof(InviteTenantAdminAsync), tenantId) is { } error)
        {
            return Task.FromResult<Result>(error);
        }

        Invites[(tenantId, email.ToLowerInvariant())] = true;
        return Task.FromResult(Result.Success());
    }

    public Task<Result<bool>> HasSignedInAsync(TenantId tenantId, string email, CancellationToken ct) =>
        Task.FromResult(Record(nameof(HasSignedInAsync), tenantId) is { } error
            ? (Result<bool>)error
            : SignedIn.ContainsKey((tenantId, email.ToLowerInvariant())));

    public Task<Result<ProvisionedProvider>> UpsertOidcProviderAsync(TenantId tenantId, OidcProviderSpec spec, CancellationToken ct) =>
        Upsert(nameof(UpsertOidcProviderAsync), tenantId, new ProvisionedProvider(
            $"{tenantId.Value}-oidc", IdpKind.Oidc, $"http://localhost:8080/realms/gatehouse/broker/{tenantId.Value}-oidc/endpoint", null));

    public Task<Result<ProvisionedProvider>> UpsertSamlProviderAsync(TenantId tenantId, SamlProviderSpec spec, CancellationToken ct) =>
        Upsert(nameof(UpsertSamlProviderAsync), tenantId, new ProvisionedProvider(
            $"{tenantId.Value}-saml", IdpKind.Saml, $"http://localhost:8080/realms/gatehouse/broker/{tenantId.Value}-saml/endpoint", "http://localhost:8080/realms/gatehouse"));

    public Task<Result> RemoveProviderAsync(TenantId tenantId, CancellationToken ct)
    {
        if (Record(nameof(RemoveProviderAsync), tenantId) is { } error)
        {
            return Task.FromResult<Result>(error);
        }

        return Task.FromResult(Providers.TryRemove(tenantId, out _)
            ? Result.Success()
            : Error.NotFound("idp.not_configured", "No identity provider is configured."));
    }

    public Task<Result<ProvisionedProvider?>> GetProviderAsync(TenantId tenantId, CancellationToken ct) =>
        Task.FromResult(Record(nameof(GetProviderAsync), tenantId) is { } error
            ? (Result<ProvisionedProvider?>)error
            : Providers.TryGetValue(tenantId, out var p) ? p : null);

    private Task<Result<ProvisionedProvider>> Upsert(string method, TenantId tenantId, ProvisionedProvider provider)
    {
        if (Record(method, tenantId) is { } error)
        {
            return Task.FromResult<Result<ProvisionedProvider>>(error);
        }

        Providers[tenantId] = provider;
        return Task.FromResult<Result<ProvisionedProvider>>(provider);
    }

    private Error? Record(string method, TenantId tenantId)
    {
        Calls.Enqueue($"{method}:{tenantId.Value}");
        return _failures.TryRemove(method, out var code) ? Error.Failure(code, $"{method} failed (fake).") : null;
    }
}
