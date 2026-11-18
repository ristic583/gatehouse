using Microsoft.Data.Sqlite;

namespace Gatehouse.Agent.Storage;

public sealed class AgentDatabase
{
    // WAL: commands of different partitions run in parallel (PollLoop), so readers must not wait for a writer.
    private const string Ddl = """
        PRAGMA journal_mode = WAL;
        CREATE TABLE IF NOT EXISTS processed_commands (
            idempotency_key TEXT PRIMARY KEY, command_id TEXT NOT NULL, status TEXT NOT NULL,
            pacs_ref TEXT NULL, error_json TEXT NULL, processed_at TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS pending_acks (
            command_id TEXT PRIMARY KEY, ack_json TEXT NOT NULL, created_at TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS adapter_cursors (name TEXT PRIMARY KEY, value TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS scheduled_revocations (
            id INTEGER PRIMARY KEY AUTOINCREMENT, person_ref TEXT NOT NULL, card_number TEXT NOT NULL,
            vendor_access_level_ref TEXT NOT NULL, revoke_at TEXT NOT NULL, done INTEGER NOT NULL DEFAULT 0);
        CREATE UNIQUE INDEX IF NOT EXISTS ux_scheduled_revocations_pending
            ON scheduled_revocations(person_ref, vendor_access_level_ref) WHERE done = 0;
        CREATE TABLE IF NOT EXISTS refused_removals (
            person_ref TEXT NOT NULL, vendor_access_level_ref TEXT NOT NULL, code TEXT NOT NULL, first_refused_at TEXT NOT NULL,
            attempts INTEGER NOT NULL, PRIMARY KEY (person_ref, vendor_access_level_ref));
        CREATE TABLE IF NOT EXISTS fences (
            person_ref TEXT NOT NULL, vendor_access_level_ref TEXT NOT NULL, sequence INTEGER NOT NULL, created_at TEXT NOT NULL,
            PRIMARY KEY (person_ref, vendor_access_level_ref));
        CREATE TABLE IF NOT EXISTS pending_events (event_id TEXT PRIMARY KEY, occurred_at TEXT NOT NULL, json TEXT NOT NULL);
        """;

    private readonly string _connectionString;
    private bool _created;

    public AgentDatabase(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(dataDirectory, "agent.db"),
            Pooling = false,
        }.ToString();
    }

    public async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(ct);
        if (!_created)
        {
            await using var ddl = connection.CreateCommand();
            ddl.CommandText = Ddl;
            await ddl.ExecuteNonQueryAsync(ct);
            _created = true;
        }

        return connection;
    }
}
