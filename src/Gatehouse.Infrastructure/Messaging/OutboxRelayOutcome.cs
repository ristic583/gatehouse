namespace Gatehouse.Infrastructure.Messaging;

/// <summary>One relay pass over a tenant: rows published, and when its earliest pending row is due (<c>null</c>: none left).</summary>
public sealed record OutboxRelayOutcome(int Published, DateTimeOffset? NextAttemptAt);
