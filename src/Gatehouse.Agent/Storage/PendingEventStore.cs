using System.Globalization;
using System.Text.Json;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Adapters;
using Gatehouse.Connector.Contracts.Events;

namespace Gatehouse.Agent.Storage;

/// <summary>Door events read from the adapter and not yet accepted by the gateway (at-least-once upload).</summary>
public sealed class PendingEventStore(AgentDatabase db)
{
    /// <summary>Stores the event; an event id already pending is ignored.</summary>
    public async Task AddAsync(PacsDoorEvent ev, CancellationToken ct)
    {
        var dto = new DoorEvent(
            ev.EventId, ev.OccurredAt, ev.Type, ev.PacsDoorRef, ev.PacsReaderRef, ev.CardNumber, ev.PersonRef, ev.Direction);
        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO pending_events (event_id, occurred_at, json) VALUES ($id, $t, $j)";
        cmd.Parameters.AddWithValue("$id", ev.EventId.ToString());
        cmd.Parameters.AddWithValue("$t", ev.OccurredAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$j", JsonSerializer.Serialize(dto, ConnectorJson.Options));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Oldest events first (by occurrence, then by arrival).</summary>
    public async Task<IReadOnlyList<DoorEvent>> TakeAsync(int max, CancellationToken ct)
    {
        var list = new List<DoorEvent>();
        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT json FROM pending_events ORDER BY occurred_at, rowid LIMIT $n";
        cmd.Parameters.AddWithValue("$n", max);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(JsonSerializer.Deserialize<DoorEvent>(r.GetString(0), ConnectorJson.Options)!);
        }

        return list;
    }

    public async Task RemoveAsync(IEnumerable<Guid> eventIds, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        await using var tx = c.BeginTransaction();
        await using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM pending_events WHERE event_id = $id";
        var p = cmd.Parameters.Add("$id", Microsoft.Data.Sqlite.SqliteType.Text);
        foreach (var id in eventIds)
        {
            p.Value = id.ToString();
            await cmd.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    public async Task<int> CountAsync(CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM pending_events";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
    }
}
