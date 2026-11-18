using Gatehouse.Domain.Common;

namespace Gatehouse.Domain.Platform;

public sealed class TenantProvisioning
{
    public const int LastErrorMaxLength = 1000;

    private TenantProvisioning()
    {
        AdminEmail = string.Empty;
        AdminGivenName = string.Empty;
        AdminFamilyName = string.Empty;
        TimeZoneId = string.Empty;
    }

    public TenantId TenantId { get; private set; }

    public ProvisioningStep Step { get; private set; }

    public ProvisioningState State { get; private set; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    public string AdminEmail { get; private set; }

    public string AdminGivenName { get; private set; }

    public string AdminFamilyName { get; private set; }

    public string TimeZoneId { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset? AdminInvitedAt { get; private set; }

    public DateTimeOffset? AdminActivatedAt { get; private set; }

    public static TenantProvisioning Start(
        TenantId tenantId,
        string adminEmail,
        string adminGivenName,
        string adminFamilyName,
        string timeZoneId,
        DateTimeOffset now) => new()
        {
            TenantId = tenantId,
            Step = ProvisioningStep.Database,
            State = ProvisioningState.Running,
            AdminEmail = adminEmail,
            AdminGivenName = adminGivenName,
            AdminFamilyName = adminFamilyName,
            TimeZoneId = timeZoneId,
            StartedAt = now,
            UpdatedAt = now,
        };

    public Result Advance(DateTimeOffset now)
    {
        if (State != ProvisioningState.Running)
        {
            return Error.Conflict("tenant.invalid_status_transition", "Provisioning is not running.");
        }

        if (Step == ProvisioningStep.AdminInvite)
        {
            AdminInvitedAt = now;
        }

        Step++;
        Attempts = 0;
        LastError = null;
        UpdatedAt = now;
        if (Step == ProvisioningStep.Completed)
        {
            State = ProvisioningState.Completed;
            CompletedAt = now;
        }

        return Result.Success();
    }

    public void Fail(string error, DateTimeOffset now)
    {
        State = ProvisioningState.Failed;
        Attempts++;
        LastError = error is { Length: > LastErrorMaxLength } ? error[..LastErrorMaxLength] : error;
        UpdatedAt = now;
    }

    public Result Retry(DateTimeOffset now)
    {
        if (State != ProvisioningState.Failed)
        {
            return Error.Conflict("tenant.provisioning_not_failed", "Provisioning has not failed.");
        }

        State = ProvisioningState.Running;
        UpdatedAt = now;
        return Result.Success();
    }

    public void MarkAdminActivated(DateTimeOffset now) => AdminActivatedAt ??= now;
}
