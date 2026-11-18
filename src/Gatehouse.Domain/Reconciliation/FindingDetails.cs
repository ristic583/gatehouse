namespace Gatehouse.Domain.Reconciliation;

/// <summary>Finding details stored as jsonb: differing fields (<c>cardNumber</c>, <c>validFrom</c>, <c>validTo</c>) and both
/// sides.</summary>
public sealed record FindingDetails(IReadOnlyList<string> Differences, FindingValues? Expected, FindingValues? Actual);
