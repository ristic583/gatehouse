using Gatehouse.Domain.Common;

namespace Gatehouse.Infrastructure.Messaging;

/// <summary>Without Redis there is no cross-process signal: the relay visits every tenant on every tick (pre-M11 behaviour).</summary>
public sealed class PollingOutboxSignal : IOutboxSignal
{
    public void Notify(TenantId tenant)
    {
    }

    public Task<IReadOnlySet<TenantId>?> TakeAsync(IReadOnlyCollection<TenantId> tenants, CancellationToken ct) =>
        Task.FromResult<IReadOnlySet<TenantId>?>(null);
}
