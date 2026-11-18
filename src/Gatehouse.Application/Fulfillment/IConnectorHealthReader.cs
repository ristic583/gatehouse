using Gatehouse.Domain.Fulfillment;

namespace Gatehouse.Application.Fulfillment;

public interface IConnectorHealthReader
{
    public Task<IReadOnlyDictionary<PacsInstanceId, ConnectorHealth>> GetAsync(
        IReadOnlyCollection<PacsInstanceId> instanceIds, CancellationToken ct);
}

public sealed record ConnectorHealth(bool Online, DateTimeOffset? LastHeartbeatAt);
