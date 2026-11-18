namespace Gatehouse.Domain.Access;

public sealed record AccessRequestDecision(DateTimeOffset EffectiveEnd, bool CappedToEligibility);
