using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;

namespace Gatehouse.Application.Connectors;

/// <summary>Next per-(instance, partition) command sequence; runs in the caller's transaction (domain-model.md §2.5).</summary>
public interface IConnectorSequencer
{
    public Task<long> NextAsync(PacsInstanceId pacsInstanceId, string partitionKey, CancellationToken ct);
}
