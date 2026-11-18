using System.Text.Json;
using Npgsql;

namespace LegacyPacs.Simulator;

/// <summary>The "controller service": works through unprocessed integration_inbox rows in id order, honouring chaos settings.</summary>
public sealed class InboxProcessor(NpgsqlDataSource dataSource, TimeProvider? timeProvider = null)
{
    private readonly DoorController _doors = new(dataSource, timeProvider ?? TimeProvider.System);

    public async Task<int> ProcessOnceAsync(CancellationToken ct)
    {
        var rows = new List<(long Id, string Kind, string Payload)>();
        await using (var select = dataSource.CreateCommand("SELECT id, kind, payload::text FROM integration_inbox WHERE processed_at IS NULL ORDER BY id"))
        await using (var reader = await select.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                rows.Add((reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));
            }
        }

        foreach (var (id, kind, payload) in rows)
        {
            var error = await ProcessRowAsync(kind, payload, ct);
            await using var done = dataSource.CreateCommand("UPDATE integration_inbox SET processed_at = now(), error = $2 WHERE id = $1");
            done.Parameters.AddWithValue(id);
            done.Parameters.AddWithValue((object?)error ?? DBNull.Value);
            await done.ExecuteNonQueryAsync(ct);
        }

        return rows.Count;
    }

    private async Task<string?> ProcessRowAsync(string kind, string payload, CancellationToken ct)
    {
        int delay;
        int transient;
        int permanent;
        await using (var chaos = dataSource.CreateCommand("""
            UPDATE chaos_settings c SET
                fail_next = CASE WHEN o.fail_next > 0 THEN o.fail_next - 1 ELSE 0 END,
                fail_next_permanent = CASE WHEN o.fail_next <= 0 AND o.fail_next_permanent > 0 THEN o.fail_next_permanent - 1 ELSE o.fail_next_permanent END
            FROM (SELECT fail_next, fail_next_permanent, delay_ms FROM chaos_settings WHERE id = 1 FOR UPDATE) o
            WHERE c.id = 1
            RETURNING o.delay_ms, o.fail_next, o.fail_next_permanent
            """))
        await using (var reader = await chaos.ExecuteReaderAsync(ct))
        {
            await reader.ReadAsync(ct);
            delay = reader.GetInt32(0);
            transient = reader.GetInt32(1);
            permanent = reader.GetInt32(2);
        }

        if (delay > 0)
        {
            await Task.Delay(delay, ct);
        }

        if (transient > 0)
        {
            return "TRANSIENT: simulated outage";
        }

        if (permanent > 0)
        {
            return "REJECTED: simulated rejection";
        }

        using var doc = JsonDocument.Parse(payload);
        var p = doc.RootElement;
        return kind switch
        {
            "UpsertCardholder" => await UpsertCardholderAsync(p, ct),
            "GrantAccess" => await GrantAsync(p, ct),
            "RevokeAccess" => await RevokeAsync(p, ct),
            "SetCardState" => await SetCardStateAsync(p, ct),
            "DoorCommand" => await _doors.ApplyCommandAsync(p, ct),
            _ => $"UNSUPPORTED_KIND: {kind}",
        };
    }

    private async Task<string?> UpsertCardholderAsync(JsonElement p, CancellationToken ct)
    {
        var person = Str(p, "personRef");
        await ExecAsync("INSERT INTO cardholders (person_ref, given_name, family_name) VALUES ($1, $2, $3) ON CONFLICT (person_ref) DO UPDATE SET given_name = $2, family_name = $3", ct, person, Str(p, "givenName"), Str(p, "familyName"));
        if (OptStr(p, "cardNumber") is { } card)
        {
            await ExecAsync("INSERT INTO cards (card_number, person_ref, state) VALUES ($1, $2, 'Active') ON CONFLICT (card_number) DO UPDATE SET person_ref = $2", ct, card, person);
        }

        return null;
    }

    private async Task<string?> GrantAsync(JsonElement p, CancellationToken ct)
    {
        var level = Str(p, "vendorAccessLevelRef");
        await using (var exists = dataSource.CreateCommand("SELECT 1 FROM access_levels WHERE ref = $1"))
        {
            exists.Parameters.AddWithValue(level);
            if (await exists.ExecuteScalarAsync(ct) is null)
            {
                return "UNKNOWN_ACCESS_LEVEL";
            }
        }

        await using var command = dataSource.CreateCommand("""
            INSERT INTO access_assignments (person_ref, card_number, access_level_ref, valid_from, valid_to, is_active)
            VALUES ($1, $2, $3, $4, $5, true)
            ON CONFLICT (person_ref, access_level_ref) DO UPDATE SET card_number = $2, valid_from = $4, valid_to = $5, is_active = true
            """);
        command.Parameters.AddWithValue(Str(p, "personRef"));
        command.Parameters.AddWithValue(OptStr(p, "cardNumber") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue(level);
        command.Parameters.AddWithValue(OptDate(p, "validFrom") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue(OptDate(p, "validTo") ?? (object)DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
        return null;
    }

    private async Task<string?> RevokeAsync(JsonElement p, CancellationToken ct)
    {
        await ExecAsync("UPDATE access_assignments SET is_active = false WHERE person_ref = $1 AND access_level_ref = $2", ct, Str(p, "personRef"), Str(p, "vendorAccessLevelRef"));
        return null;
    }

    private async Task<string?> SetCardStateAsync(JsonElement p, CancellationToken ct)
    {
        await ExecAsync("UPDATE cards SET state = $2 WHERE card_number = $1", ct, Str(p, "cardNumber"), Str(p, "state"));
        return null;
    }

    private async Task ExecAsync(string sql, CancellationToken ct, params object[] args)
    {
        await using var command = dataSource.CreateCommand(sql);
        foreach (var a in args)
        {
            command.Parameters.AddWithValue(a);
        }

        await command.ExecuteNonQueryAsync(ct);
    }

    private static string Str(JsonElement p, string name) => p.GetProperty(name).GetString() ?? string.Empty;

    private static string? OptStr(JsonElement p, string name) =>
        p.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DateTimeOffset? OptDate(JsonElement p, string name) =>
        OptStr(p, name) is { } s ? DateTimeOffset.Parse(s, System.Globalization.CultureInfo.InvariantCulture) : null;
}
