namespace Gatehouse.Application.Platform;

/// <summary>Platform audit actions written by operator routes (<c>domain-model.md</c> §1).</summary>
public static class PlatformActions
{
    public const string TenantCreated = "platform.tenant_created";
    public const string TenantSuspended = "platform.tenant_suspended";
    public const string TenantReactivated = "platform.tenant_reactivated";
    public const string ProvisioningRetried = "platform.tenant_provisioning_retried";
}
