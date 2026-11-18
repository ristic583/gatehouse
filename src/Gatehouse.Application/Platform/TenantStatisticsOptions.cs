namespace Gatehouse.Application.Platform;

public sealed class TenantStatisticsOptions
{
    public const string SectionName = "TenantStatistics";

    public int IntervalMinutes { get; set; } = 5;
}
