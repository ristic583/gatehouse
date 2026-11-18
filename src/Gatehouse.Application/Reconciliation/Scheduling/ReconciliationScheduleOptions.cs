namespace Gatehouse.Application.Reconciliation.Scheduling;

/// <summary>Configuration section <c>Reconciliation</c>: how often each instance is reconciled and when a running report times
/// out.</summary>
public sealed class ReconciliationScheduleOptions
{
    public const string SectionName = "Reconciliation";

    public int IntervalDays { get; set; } = 30;

    public int TimeoutHours { get; set; } = 6;
}
