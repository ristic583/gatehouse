namespace Gatehouse.Domain.Platform;

public enum ProvisioningStep
{
    Database,
    Migrate,
    Baseline,
    FirstAdmin,
    Organization,
    AdminInvite,
    Completed,
}
