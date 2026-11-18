namespace Gatehouse.Domain.Tenancy;

public enum TenantStatus
{
    Active,
    Suspended,
    Deprovisioned,
    Provisioning,
    ProvisioningFailed,
}
