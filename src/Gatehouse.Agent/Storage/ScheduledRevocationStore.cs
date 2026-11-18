using System.Globalization;
using Gatehouse.Connector.Contracts.Events;

namespace Gatehouse.Agent.Storage;

/// <summary>SQLite <c>scheduled_revocations</c>: one pending row per (person, access level).</summary>
public sealed class ScheduledRevocationStore(AgentDatabase db)
{
    /// <summary>Inserts or updates the pending row for (person, level).</summary>
    public async Task ScheduleAsync(
        string personRef, string cardNumber, string vendorAccessLevelRef, DateTimeOffset revokeAt, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO scheduled_revocations (person_ref, card_number, vendor_access_level_ref, revoke_at, done)
            VALUES ($p, $c, $l, $t, 0)
            ON CONFLICT(person_ref, vendor_access_level_ref) WHERE done = 0
            DO UPDATE SET card_number = excluded.card_number, revoke_at = excluded.revoke_at
            """;
        cmd.Parameters.AddWithValue("$p", personRef);
        cmd.Parameters.AddWithValue("$c", cardNumber);
        cmd.Parameters.AddWithValue("$l", vendorAccessLevelRef);
        cmd.Parameters.AddWithValue("$t", Format(revokeAt));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Marks the pending row for (person, level) done, if any, and forgets its refusal.</summary>
    public async Task CancelAsync(string personRef, string vendorAccessLevelRef, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            UPDATE scheduled_revocations SET done = 1 WHERE person_ref = $p AND vendor_access_level_ref = $l AND done = 0;
            DELETE FROM refused_removals WHERE person_ref = $p AND vendor_access_level_ref = $l;
            """;
        cmd.Parameters.AddWithValue("$p", personRef);
        cmd.Parameters.AddWithValue("$l", vendorAccessLevelRef);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Pending rows with <c>revoke_at &lt;= now</c>, oldest first.</summary>
    public async Task<IReadOnlyList<ScheduledRevocation>> DueAsync(DateTimeOffset now, int limit, CancellationToken ct)
    {
        var list = new List<ScheduledRevocation>();
        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT id, person_ref, card_number, vendor_access_level_ref, revoke_at FROM scheduled_revocations
            WHERE done = 0 AND revoke_at <= $now ORDER BY revoke_at, id LIMIT $n
            """;
        cmd.Parameters.AddWithValue("$now", Format(now));
        cmd.Parameters.AddWithValue("$n", limit);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new ScheduledRevocation(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3),
                DateTimeOffset.Parse(r.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
        }

        return list;
    }

    /// <summary>Keeps the row pending but moves its next attempt to <paramref name="until"/>.</summary>
    public async Task PostponeAsync(long id, DateTimeOffset until, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE scheduled_revocations SET revoke_at = $t WHERE id = $id AND done = 0";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$t", Format(until));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task MarkDoneAsync(long id, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            DELETE FROM refused_removals WHERE (person_ref, vendor_access_level_ref) IN
                (SELECT person_ref, vendor_access_level_ref FROM scheduled_revocations WHERE id = $id);
            UPDATE scheduled_revocations SET done = 1 WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Counts one more permanent refusal of the pending removal (first time keeps <paramref name="now"/>).</summary>
    public async Task RecordRefusalAsync(
        string personRef, string vendorAccessLevelRef, string code, DateTimeOffset now, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO refused_removals (person_ref, vendor_access_level_ref, code, first_refused_at, attempts) VALUES ($p, $l, $c, $t, 1)
            ON CONFLICT (person_ref, vendor_access_level_ref) DO UPDATE SET code = excluded.code, attempts = attempts + 1
            """;
        cmd.Parameters.AddWithValue("$p", personRef);
        cmd.Parameters.AddWithValue("$l", vendorAccessLevelRef);
        cmd.Parameters.AddWithValue("$c", code);
        cmd.Parameters.AddWithValue("$t", Format(now));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Refused removals still pending, for the heartbeat (<c>refusedRemovals</c>); ref =
    /// <c>{personRef}:{vendorAccessLevelRef}</c>.</summary>
    public async Task<IReadOnlyList<RefusedRemoval>> RefusedAsync(CancellationToken ct)
    {
        var list = new List<RefusedRemoval>();
        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT person_ref, vendor_access_level_ref, code, first_refused_at, attempts FROM "
            + "refused_removals ORDER BY first_refused_at";
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            list.Add(new RefusedRemoval($"{r.GetString(0)}:{r.GetString(1)}", r.GetString(2),
                DateTimeOffset.Parse(r.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), r.GetInt32(4)));
        }

        return list;
    }

    public async Task<int> PendingCountAsync(CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM scheduled_revocations WHERE done = 0";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
    }

    // Fixed-width UTC round-trip format: lexical order == chronological order.
    private static string Format(DateTimeOffset value) => value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);
}
