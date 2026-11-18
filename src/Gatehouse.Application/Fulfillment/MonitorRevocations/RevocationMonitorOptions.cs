namespace Gatehouse.Application.Fulfillment.MonitorRevocations;

public sealed class RevocationMonitorOptions
{
    public const string SectionName = "Gatehouse:RevocationMonitor";

    public TimeSpan AlertAfter { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan EmergencyAlertAfter { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(60);
}
