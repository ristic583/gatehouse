namespace Gatehouse.Domain.Reconciliation;

/// <summary>One side of a finding's comparison; the card number is always masked.</summary>
public sealed record FindingValues(string? CardNumberMasked, DateTimeOffset? ValidFrom, DateTimeOffset? ValidTo);
