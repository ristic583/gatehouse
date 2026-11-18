using System.Text.RegularExpressions;
using Gatehouse.Domain.Common;

namespace Gatehouse.Domain.Tenancy;

public sealed partial class Tenant : AggregateRoot<TenantId>
{
    public const int DisplayNameMaxLength = 100;

    private Tenant(
        TenantId id,
        string displayName,
        IdpKind idpKind,
        string encryptedConnectionString,
        DateTimeOffset createdAt)
        : base(id)
    {
        DisplayName = displayName;
        IdpKind = idpKind;
        EncryptedConnectionString = encryptedConnectionString;
        CreatedAt = createdAt;
        Status = TenantStatus.Active;
        DatabaseName = id.DatabaseName;
        KeycloakOrganizationAlias = id.Value;
    }

    // EF Core
    private Tenant()
        : base(default)
    {
        DisplayName = string.Empty;
        DatabaseName = string.Empty;
        EncryptedConnectionString = string.Empty;
        KeycloakOrganizationAlias = string.Empty;
    }

    public string DisplayName { get; private set; }

    public TenantStatus Status { get; private set; }

    public string DatabaseName { get; private set; }

    [Sensitive]
    public string EncryptedConnectionString { get; private set; }

    public IdpKind IdpKind { get; private set; }

    public string KeycloakOrganizationAlias { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public string? EmailDomain { get; private set; }

    public DateTimeOffset? SuspendedAt { get; private set; }

    public string? SuspendedReason { get; private set; }

    public static Result<Tenant> Create(
        TenantId id,
        string displayName,
        IdpKind idpKind,
        string encryptedConnectionString,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > DisplayNameMaxLength)
        {
            return Error.Validation("tenant.invalid_display_name", "Tenant display name must be 1-100 characters.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(encryptedConnectionString);

        return new Tenant(id, displayName, idpKind, encryptedConnectionString, now);
    }

    public static Result<Tenant> CreateProvisioning(
        TenantId id,
        string displayName,
        string emailDomain,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > DisplayNameMaxLength)
        {
            return Error.Validation("tenant.invalid_display_name", "Tenant display name must be 1-100 characters.");
        }

        emailDomain = emailDomain?.Trim().ToLowerInvariant() ?? string.Empty;
        if (emailDomain.Length > 253 || !EmailDomainPattern().IsMatch(emailDomain))
        {
            return Error.Validation("tenant.invalid_email_domain", "Email domain must be a DNS name such as example.com.");
        }

        var tenant = new Tenant(id, displayName, IdpKind.Local, string.Empty, now)
        {
            Status = TenantStatus.Provisioning,
            EmailDomain = emailDomain,
        };
        return tenant;
    }

    public void SetConnection(string encryptedConnectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(encryptedConnectionString);
        EncryptedConnectionString = encryptedConnectionString;
    }

    public void SetIdpKind(IdpKind kind) => IdpKind = kind;

    public Result MarkProvisioned() => Transition(TenantStatus.Provisioning, TenantStatus.Active);

    public Result MarkProvisioningFailed() => Transition(TenantStatus.Provisioning, TenantStatus.ProvisioningFailed);

    public Result RetryProvisioning() => Transition(TenantStatus.ProvisioningFailed, TenantStatus.Provisioning);

    public Result Suspend(string reason, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Length > 500)
        {
            return Error.Validation("validation.failed", "Suspension reason must be 1-500 characters.");
        }

        var result = Transition(TenantStatus.Active, TenantStatus.Suspended);
        if (result.IsSuccess)
        {
            SuspendedAt = now;
            SuspendedReason = reason;
        }

        return result;
    }

    public Result Reactivate()
    {
        var result = Transition(TenantStatus.Suspended, TenantStatus.Active);
        if (result.IsSuccess)
        {
            SuspendedAt = null;
            SuspendedReason = null;
        }

        return result;
    }

    private Result Transition(TenantStatus from, TenantStatus to)
    {
        if (Status != from)
        {
            return Error.Conflict("tenant.invalid_status_transition", $"Tenant cannot move from {Status} to {to}.");
        }

        Status = to;
        return Result.Success();
    }

    [GeneratedRegex(@"^(?=.{1,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z][a-z0-9-]{0,61}[a-z0-9]$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailDomainPattern();
}
