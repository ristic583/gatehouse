using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;

namespace Gatehouse.Application.Connectors;

/// <summary>Allows one open poll per PACS instance.</summary>
public interface IConnectorPollGate
{
    /// <summary>Returns a lease to dispose when the poll ends, or null when another poll is open.</summary>
    public Task<IAsyncDisposable?> TryAcquireAsync(TenantId tenantId, PacsInstanceId pacsInstanceId, TimeSpan ttl, CancellationToken ct);
}
