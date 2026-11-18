namespace Gatehouse.Application.Platform.Provisioning;

/// <summary>Audit actions written by provisioning (<c>domain-model.md</c> §1, §2.10).</summary>
public static class ProvisioningActions
{
    public const string TenantProvisioned = "platform.tenant_provisioned";
    public const string TenantProvisioningFailed = "platform.tenant_provisioning_failed";
    public const string AdminInviteSent = "platform.admin_invite_sent";

    /// <summary>First entry of a new tenant's own audit chain (system actor <c>platform</c>).</summary>
    public const string TenantChainProvisioned = "tenant.provisioned";
}
