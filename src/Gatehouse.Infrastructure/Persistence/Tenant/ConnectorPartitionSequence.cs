using Gatehouse.Domain.Fulfillment;

namespace Gatehouse.Infrastructure.Persistence.Tenant;

public sealed class ConnectorPartitionSequence
{
    public PacsInstanceId PacsInstanceId { get; set; }

    public string PartitionKey { get; set; } = string.Empty;

    public long LastSequence { get; set; }
}
