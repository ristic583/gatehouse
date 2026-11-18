using System.Globalization;
using Gatehouse.Connector.Contracts.Events;

namespace Gatehouse.Agent.Storage;

public sealed class ProcessedCommandStore(AgentDatabase db)
{
    public async Task<StoredResult?> GetAsync(string key, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT status, pacs_ref, error_json, processed_at FROM processed_commands WHERE idempotency_key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct))
        {
            return null;
        }

        return new StoredResult(r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2),
            DateTimeOffset.Parse(r.GetString(3), CultureInfo.InvariantCulture));
    }

    public async Task SaveAsync(CommandAcked ack, string? errorJson, DateTimeOffset now, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO processed_commands (idempotency_key, command_id, status, pacs_ref, error_json, "
            + "processed_at) VALUES ($k, $id, $s, $p, $e, $t)";
        cmd.Parameters.AddWithValue("$k", ack.IdempotencyKey);
        cmd.Parameters.AddWithValue("$id", ack.CommandId.ToString());
        cmd.Parameters.AddWithValue("$s", ack.Status.ToString());
        cmd.Parameters.AddWithValue("$p", (object?)ack.PacsRef ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$e", (object?)errorJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$t", now.ToString("O", CultureInfo.InvariantCulture));
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
