using System.Text.Json;
using Npgsql;

namespace LegacyPacs.Simulator;

/// <summary>The simulated door controllers: decide swipes from the PACS's own access data and apply door commands, writing <c>event_log</c>.</summary>
public sealed class DoorController(NpgsqlDataSource dataSource, TimeProvider timeProvider)
{
    public static readonly string[] EventTypes =
    [
        "AccessGranted", "AccessDenied", "DoorOpened", "DoorClosed", "DoorHeldOpen", "DoorForced",
        "LockdownStarted", "LockdownCleared", "ScheduledUnlockStarted", "ScheduledUnlockEnded", "DoorUnlocked", "DoorRelocked",
    ];

    /// <summary>Returns null when the door is unknown.</summary>
    public async Task<SwipeResult?> SwipeAsync(SwipeRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = timeProvider.GetUtcNow();
        string level;
        bool lockdown;
        await using (var door = dataSource.CreateCommand("SELECT access_level_ref, lockdown FROM doors WHERE door_ref = $1"))
        {
            door.Parameters.AddWithValue(request.DoorRef);
            await using var r = await door.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct))
            {
                return null;
            }

            level = r.GetString(0);
            lockdown = r.GetBoolean(1);
        }

        var (cardNumber, personRef, cardState) = await ResolveCardAsync(request, ct);
        var granted = !lockdown && cardState == "Active" && personRef is not null && await HasActiveAssignmentAsync(personRef, level, now, ct);
        var decision = granted ? "Granted" : "Denied";
        var eventId = await WriteEventAsync(granted ? "AccessGranted" : "AccessDenied", request.DoorRef, ReaderRef(request.DoorRef), cardNumber ?? request.CardNumber, personRef, request.Direction, now, ct);
        return new SwipeResult(decision, eventId);
    }

    /// <summary>Writes a raw door event; returns null when the door is unknown.</summary>
    public async Task<long?> RecordDoorEventAsync(string doorRef, string type, CancellationToken ct)
    {
        if (!await DoorExistsAsync(doorRef, ct))
        {
            return null;
        }

        return await WriteEventAsync(type, doorRef, null, null, null, null, timeProvider.GetUtcNow(), ct);
    }

    /// <summary>Applies an inbox <c>DoorCommand</c> payload; returns an inbox error or null.</summary>
    public async Task<string?> ApplyCommandAsync(JsonElement payload, CancellationToken ct)
    {
        var kind = payload.TryGetProperty("kind", out var k) ? k.GetString() : null;
        var refs = payload.TryGetProperty("pacsDoorRefs", out var d) && d.ValueKind == JsonValueKind.Array
            ? d.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList()
            : [];
        var unknown = new List<string>();
        foreach (var doorRef in refs)
        {
            if (!await DoorExistsAsync(doorRef, ct))
            {
                unknown.Add(doorRef);
            }
        }

        if (unknown.Count > 0)
        {
            return "UNKNOWN_DOOR: " + string.Join(',', unknown);
        }

        var now = timeProvider.GetUtcNow();
        switch (kind)
        {
            case "MomentaryUnlock":
                var seconds = payload.TryGetProperty("durationSeconds", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt32() : 5;
                foreach (var doorRef in refs)
                {
                    await ExecAsync("UPDATE doors SET unlocked_until = $2 WHERE door_ref = $1", ct, doorRef, now.AddSeconds(seconds));
                    await WriteEventAsync("DoorUnlocked", doorRef, null, null, null, null, now, ct);
                }

                return null;
            case "Lockdown":
                var action = payload.TryGetProperty("lockdownAction", out var a) ? a.GetString() : null;
                if (action is not ("Start" or "Clear"))
                {
                    return "INVALID_LOCKDOWN_ACTION";
                }

                foreach (var doorRef in refs)
                {
                    await ExecAsync("UPDATE doors SET lockdown = $2 WHERE door_ref = $1", ct, doorRef, action == "Start");
                    await WriteEventAsync(action == "Start" ? "LockdownStarted" : "LockdownCleared", doorRef, null, null, null, null, now, ct);
                }

                return null;
            default:
                return $"UNSUPPORTED_DOOR_COMMAND: {kind}";
        }
    }

    /// <summary>Relocks doors whose momentary unlock has elapsed; returns the number relocked.</summary>
    public async Task<int> RelockDueAsync(CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var due = new List<string>();
        await using (var command = dataSource.CreateCommand("UPDATE doors SET unlocked_until = NULL WHERE unlocked_until <= $1 RETURNING door_ref"))
        {
            command.Parameters.AddWithValue(now);
            await using var r = await command.ExecuteReaderAsync(ct);
            while (await r.ReadAsync(ct))
            {
                due.Add(r.GetString(0));
            }
        }

        foreach (var doorRef in due)
        {
            await WriteEventAsync("DoorRelocked", doorRef, null, null, null, null, now, ct);
        }

        return due.Count;
    }

    public async Task<IReadOnlyList<DoorRow>> ListDoorsAsync(CancellationToken ct)
    {
        var rows = new List<DoorRow>();
        await using var command = dataSource.CreateCommand("SELECT door_ref, access_level_ref, lockdown, unlocked_until FROM doors ORDER BY door_ref");
        await using var r = await command.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            rows.Add(new DoorRow(r.GetString(0), r.GetString(1), r.GetBoolean(2), r.IsDBNull(3) ? null : r.GetFieldValue<DateTimeOffset>(3)));
        }

        return rows;
    }

    private static string ReaderRef(string doorRef) => "R-" + (doorRef.StartsWith("D-", StringComparison.Ordinal) ? doorRef[2..] : doorRef);

    private async Task<(string? CardNumber, string? PersonRef, string? State)> ResolveCardAsync(SwipeRequest request, CancellationToken ct)
    {
        string sql;
        string key;
        if (!string.IsNullOrWhiteSpace(request.CardNumber))
        {
            sql = "SELECT card_number, person_ref, state FROM cards WHERE card_number = $1";
            key = request.CardNumber;
        }
        else if (!string.IsNullOrWhiteSpace(request.PersonRef))
        {
            sql = "SELECT card_number, person_ref, state FROM cards WHERE person_ref = $1 ORDER BY (state = 'Active') DESC, card_number LIMIT 1";
            key = request.PersonRef;
        }
        else if (!string.IsNullOrWhiteSpace(request.HolderName))
        {
            sql = """
                SELECT c.card_number, c.person_ref, c.state FROM cards c JOIN cardholders h ON h.person_ref = c.person_ref
                WHERE h.given_name || ' ' || h.family_name = $1 ORDER BY (c.state = 'Active') DESC, c.card_number LIMIT 1
                """;
            key = request.HolderName;
        }
        else
        {
            return (null, null, null);
        }

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(key);
        await using var r = await command.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? (r.GetString(0), r.GetString(1), r.GetString(2)) : (null, request.PersonRef, null);
    }

    private async Task<bool> HasActiveAssignmentAsync(string personRef, string level, DateTimeOffset now, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            SELECT 1 FROM access_assignments
            WHERE person_ref = $1 AND access_level_ref = $2 AND is_active
              AND (valid_from IS NULL OR valid_from <= $3) AND (valid_to IS NULL OR valid_to > $3)
            """);
        command.Parameters.AddWithValue(personRef);
        command.Parameters.AddWithValue(level);
        command.Parameters.AddWithValue(now);
        return await command.ExecuteScalarAsync(ct) is not null;
    }

    private async Task<bool> DoorExistsAsync(string doorRef, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("SELECT 1 FROM doors WHERE door_ref = $1");
        command.Parameters.AddWithValue(doorRef);
        return await command.ExecuteScalarAsync(ct) is not null;
    }

    private async Task<long> WriteEventAsync(string type, string doorRef, string? readerRef, string? cardNumber, string? personRef, string? direction, DateTimeOffset at, CancellationToken ct)
    {
        await using var command = dataSource.CreateCommand("""
            INSERT INTO event_log (occurred_at, type, door_ref, reader_ref, card_number, person_ref, direction)
            VALUES ($1, $2, $3, $4, $5, $6, $7) RETURNING id
            """);
        command.Parameters.AddWithValue(at);
        command.Parameters.AddWithValue(type);
        command.Parameters.AddWithValue(doorRef);
        command.Parameters.AddWithValue((object?)readerRef ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)cardNumber ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)personRef ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)direction ?? DBNull.Value);
        return (long)(await command.ExecuteScalarAsync(ct))!;
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
}
