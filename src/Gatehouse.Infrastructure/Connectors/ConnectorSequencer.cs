using Gatehouse.Application.Connectors;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Infrastructure.Connectors;

public sealed class ConnectorSequencer(TenantDbContext db) : IConnectorSequencer
{
    public async Task<long> NextAsync(PacsInstanceId pacsInstanceId, string partitionKey, CancellationToken ct)
    {
        var id = pacsInstanceId.Value;
        var rows = await db.Database.SqlQuery<long>($"""
            INSERT INTO connector_partition_sequences (pacs_instance_id, partition_key, last_sequence)
            VALUES ({id}, {partitionKey}, 1)
            ON CONFLICT (pacs_instance_id, partition_key) DO UPDATE SET last_sequence = connector_partition_sequences.last_sequence + 1
            RETURNING last_sequence AS "Value"
            """).ToListAsync(ct);
        return rows[0];
    }
}
