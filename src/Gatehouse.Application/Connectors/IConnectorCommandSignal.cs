using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;

namespace Gatehouse.Application.Connectors;

/// <summary>Wakes long-polling connectors when a command is queued.</summary>
public interface IConnectorCommandSignal
{
    public Task NotifyAsync(TenantId tenantId, PacsInstanceId pacsInstanceId, CancellationToken ct);

    public Task WaitAsync(TenantId tenantId, PacsInstanceId pacsInstanceId, TimeSpan timeout, CancellationToken ct);
}
