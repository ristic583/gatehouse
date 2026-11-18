using Gatehouse.Domain.Common;

namespace Gatehouse.Application.Connectors;

/// <summary>Short-lived dedupe of uploaded door events by event id (Redis <c>tenant:{tenantId}:door-event:{eventId}</c>, 10 min).</summary>
public interface IDoorEventDeduplicator
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    /// <summary>True when the event id was not seen in the window (and is now claimed).</summary>
    public Task<bool> TryClaimAsync(TenantId tenantId, Guid eventId, CancellationToken ct);

    /// <summary>Releases claims whose events could not be saved, so a retry is accepted.</summary>
    public Task ReleaseAsync(TenantId tenantId, IReadOnlyCollection<Guid> eventIds, CancellationToken ct);
}
