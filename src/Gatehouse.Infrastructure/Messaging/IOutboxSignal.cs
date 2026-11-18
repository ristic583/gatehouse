using Gatehouse.Domain.Common;

namespace Gatehouse.Infrastructure.Messaging;

/// <summary>
/// "This tenant has pending outbox rows": set after a commit that wrote <c>outbox_messages</c>, taken by the relay so it only visits
/// signalled tenants instead of reconnecting to every tenant database on every tick. A lost signal is covered by the relay's slow sweep.
/// </summary>
public interface IOutboxSignal
{
    /// <summary>Marks the tenant as having pending rows. Called after commit; never throws (a lost signal waits for the sweep).</summary>
    public void Notify(TenantId tenant);

    /// <summary>
    /// Returns the tenants of <paramref name="tenants"/> that were signalled and clears their signal, or <c>null</c> when signals are
    /// unavailable (no Redis, Redis error): the relay then visits every tenant.
    /// </summary>
    public Task<IReadOnlySet<TenantId>?> TakeAsync(IReadOnlyCollection<TenantId> tenants, CancellationToken ct);
}
