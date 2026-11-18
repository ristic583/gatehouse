using Npgsql;

namespace LegacyPacs.Simulator;

public static class LegacyPacsSchema
{
    public static readonly string[] AccessLevels =
    [
        "CPH-LOBBY", "CPH-OFFICE-0", "CPH-OFFICE-1", "CPH-LAB", "CPH-OFFICE-2", "CPH-PLANT", "CPH-SERVER", "CPH-VAULT",
    ];

    private const string Ddl = """
        CREATE TABLE IF NOT EXISTS integration_inbox (
            id bigserial PRIMARY KEY, kind text NOT NULL, payload jsonb NOT NULL,
            created_at timestamptz NOT NULL DEFAULT now(), processed_at timestamptz NULL, error text NULL);
        CREATE TABLE IF NOT EXISTS cardholders (person_ref text PRIMARY KEY, given_name text NOT NULL, family_name text NOT NULL);
        CREATE TABLE IF NOT EXISTS cards (card_number text PRIMARY KEY, person_ref text NOT NULL, state text NOT NULL);
        CREATE TABLE IF NOT EXISTS access_levels (ref text PRIMARY KEY, name text NOT NULL);
        CREATE TABLE IF NOT EXISTS access_assignments (
            id bigserial PRIMARY KEY, person_ref text NOT NULL, card_number text NULL, access_level_ref text NOT NULL,
            valid_from timestamptz NULL, valid_to timestamptz NULL, is_active boolean NOT NULL DEFAULT true,
            UNIQUE (person_ref, access_level_ref));
        CREATE TABLE IF NOT EXISTS event_log (
            id bigserial PRIMARY KEY, occurred_at timestamptz NOT NULL, type text NOT NULL, door_ref text NULL,
            reader_ref text NULL, card_number text NULL, person_ref text NULL, direction text NULL);
        CREATE TABLE IF NOT EXISTS chaos_settings (
            id int PRIMARY KEY, fail_next int NOT NULL DEFAULT 0, fail_next_permanent int NOT NULL DEFAULT 0, delay_ms int NOT NULL DEFAULT 0);
        INSERT INTO chaos_settings (id) VALUES (1) ON CONFLICT DO NOTHING;
        CREATE TABLE IF NOT EXISTS doors (
            door_ref text PRIMARY KEY, access_level_ref text NOT NULL, lockdown boolean NOT NULL DEFAULT false, unlocked_until timestamptz NULL);
        """;

    public static async Task EnsureAsync(NpgsqlDataSource dataSource, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        await using var command = dataSource.CreateCommand(Ddl);
        await command.ExecuteNonQueryAsync(ct);
        foreach (var code in AccessLevels)
        {
            await using var seed = dataSource.CreateCommand("INSERT INTO access_levels (ref, name) VALUES ($1, $2) ON CONFLICT DO NOTHING");
            seed.Parameters.AddWithValue("AL-" + code);
            seed.Parameters.AddWithValue(code);
            await seed.ExecuteNonQueryAsync(ct);
            await using var door = dataSource.CreateCommand("INSERT INTO doors (door_ref, access_level_ref) VALUES ($1, $2) ON CONFLICT DO NOTHING");
            door.Parameters.AddWithValue("D-" + code);
            door.Parameters.AddWithValue("AL-" + code);
            await door.ExecuteNonQueryAsync(ct);
        }
    }
}
