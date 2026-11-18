using System.Globalization;

namespace Gatehouse.Agent.Storage;

/// <summary>
/// SQLite <c>fences</c>: the highest sequence of a High priority revoke per (person, access level). Grants of that pair with a lower
/// sequence are not executed (GH-CONN-02, <c>adapter-interface.md</c> §7). Rows older than <see cref="Retention"/> are pruned.
/// </summary>
public sealed class FenceStore(AgentDatabase db)
{
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);

    public async Task RecordAsync(string personRef, string vendorAccessLevelRef, long sequence, DateTimeOffset now, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            DELETE FROM fences WHERE created_at < $cutoff;
            INSERT INTO fences (person_ref, vendor_access_level_ref, sequence, created_at) VALUES ($p, $l, $s, $t)
            ON CONFLICT (person_ref, vendor_access_level_ref) DO UPDATE
                SET sequence = max(sequence, excluded.sequence), created_at = excluded.created_at;
            """;
        cmd.Parameters.AddWithValue("$cutoff", (now - Retention).ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$p", personRef);
        cmd.Parameters.AddWithValue("$l", vendorAccessLevelRef);
        cmd.Parameters.AddWithValue("$s", sequence);
        cmd.Parameters.AddWithValue("$t", now.ToString("O", CultureInfo.InvariantCulture));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    /// <summary>True when a fence for the pair has a higher sequence than <paramref name="grantSequence"/> and is still retained.</summary>
    public async Task<bool> IsFencedAsync(
        string personRef, string vendorAccessLevelRef, long grantSequence, DateTimeOffset now, CancellationToken ct)
    {
        await using var c = await db.OpenAsync(ct);
        await using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT 1 FROM fences
            WHERE person_ref = $p AND vendor_access_level_ref = $l AND sequence > $s AND created_at >= $cutoff
            """;
        cmd.Parameters.AddWithValue("$p", personRef);
        cmd.Parameters.AddWithValue("$l", vendorAccessLevelRef);
        cmd.Parameters.AddWithValue("$s", grantSequence);
        cmd.Parameters.AddWithValue("$cutoff", (now - Retention).ToString("O", CultureInfo.InvariantCulture));
        return await cmd.ExecuteScalarAsync(ct) is not null;
    }
}
