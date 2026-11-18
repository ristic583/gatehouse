namespace Gatehouse.Application.Reconciliation.Scheduling;

/// <summary>What one scheduler run did in one tenant.</summary>
public sealed record ReconciliationScheduleResult(int TimedOut, int Requested);
