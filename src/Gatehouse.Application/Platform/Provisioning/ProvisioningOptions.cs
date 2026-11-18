namespace Gatehouse.Application.Platform.Provisioning;

public sealed class ProvisioningOptions
{
    public const string SectionName = "Provisioning";

    public int IntervalSeconds { get; set; } = 5;
}
