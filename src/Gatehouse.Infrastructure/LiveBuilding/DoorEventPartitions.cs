using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Infrastructure.LiveBuilding;

/// <summary>Daily partitions of <c>door_events</c> (ADR-006): <c>door_events_yyyymmdd</c>.</summary>
public static class DoorEventPartitions
{
    public const string Prefix = "door_events_";

    public static string NameFor(DateOnly day) => Prefix + day.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    /// <summary>Creates the partitions for <paramref name="days"/> UTC days starting at <paramref name="fromUtc"/> (idempotent).</summary>
    public static async Task EnsureAsync(DbContext db, DateOnly fromUtc, int days, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        for (var i = 0; i < days; i++)
        {
            var day = fromUtc.AddDays(i);
            var from = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var to = day.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            // Names and bounds are generated from a DateOnly, never from input.
            var sql = $"CREATE TABLE IF NOT EXISTS {NameFor(day)} PARTITION OF door_events "
                + $"FOR VALUES FROM ('{from} 00:00:00+00') TO ('{to} 00:00:00+00')";
            await db.Database.ExecuteSqlRawAsync(sql, ct);
        }
    }

    /// <summary>Drops daily partitions whose day is before <paramref name="cutoff"/>. Returns the number dropped.</summary>
    public static async Task<int> DropOlderThanAsync(DbContext db, DateOnly cutoff, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var names = await db.Database.SqlQueryRaw<string>(
            """
            SELECT c.relname AS "Value" FROM pg_inherits i
            JOIN pg_class c ON c.oid = i.inhrelid
            JOIN pg_class p ON p.oid = i.inhparent
            WHERE p.relname = 'door_events'
            """).ToListAsync(ct);
        var dropped = 0;
        foreach (var name in names)
        {
            if (!name.StartsWith(Prefix, StringComparison.Ordinal)
                || !DateOnly.TryParseExact(
                    name[Prefix.Length..], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                || day >= cutoff)
            {
                continue;
            }

            // The name is regenerated from the parsed DateOnly, so no catalog string reaches the SQL.
            var sql = $"DROP TABLE IF EXISTS {NameFor(day)}";
            await db.Database.ExecuteSqlRawAsync(sql, ct);
            dropped++;
        }

        return dropped;
    }

    /// <summary>Number of rows in the default partition (anything outside the daily range).</summary>
    public static async Task<long> CountDefaultRowsAsync(DbContext db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        return await db.Database.SqlQueryRaw<long>("SELECT count(*) AS \"Value\" FROM door_events_default").SingleAsync(ct);
    }
}
