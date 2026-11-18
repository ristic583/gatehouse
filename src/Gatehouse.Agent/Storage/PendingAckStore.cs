using System.Globalization;
using System.Text.Json;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Events;

namespace Gatehouse.Agent.Storage;

public sealed class PendingAckStore(AgentDatabase db)
{
    public async Task AddAsync(CommandAcked ack, DateTimeOffset now, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO pending_acks (command_id, ack_json, created_at) VALUES ($id, $j, $t)";
        cmd.Parameters.AddWithValue("$id", ack.CommandId.ToString());
        cmd.Parameters.AddWithValue("$j", JsonSerializer.Serialize(ack, ConnectorJson.Options));
        cmd.Parameters.AddWithValue("$t", now.ToString("O", CultureInfo.InvariantCulture));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<CommandAcked>> ListAsync(int max, CancellationToken ct)
    {
        var list = new List<CommandAcked>();
        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT ack_json FROM pending_acks ORDER BY created_at LIMIT $n";
        cmd.Parameters.AddWithValue("$n", max);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(JsonSerializer.Deserialize<CommandAcked>(r.GetString(0), ConnectorJson.Options)!);
        }

        return list;
    }

    public async Task RemoveAsync(Guid commandId, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM pending_acks WHERE command_id = $id";
        cmd.Parameters.AddWithValue("$id", commandId.ToString());
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<int> CountAsync(CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM pending_acks";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
    }
}
