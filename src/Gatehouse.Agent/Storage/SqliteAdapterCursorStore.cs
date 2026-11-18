using Gatehouse.Connector.Contracts.Adapters;

namespace Gatehouse.Agent.Storage;

public sealed class SqliteAdapterCursorStore(AgentDatabase db) : IAdapterCursorStore
{
    public async ValueTask<string?> GetAsync(string name, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT value FROM adapter_cursors WHERE name = $n";
        cmd.Parameters.AddWithValue("$n", name);
        return await cmd.ExecuteScalarAsync(ct) as string;
    }

    public async ValueTask SetAsync(string name, string value, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO adapter_cursors (name, value) VALUES ($n, $v)";
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$v", value);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
