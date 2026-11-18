namespace Gatehouse.Agent.Storage;

/// <summary>A pending local revocation for an adapter without native validity (adapter-interface.md §7).</summary>
public sealed record ScheduledRevocation(
    long Id, string PersonRef, string CardNumber, string VendorAccessLevelRef, DateTimeOffset RevokeAt);
