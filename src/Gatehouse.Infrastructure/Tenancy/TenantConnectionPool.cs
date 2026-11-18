using System.Collections.Concurrent;
using Gatehouse.Domain.Common;
using Npgsql;

namespace Gatehouse.Infrastructure.Tenancy;

/// <summary>
/// Pool settings for tenant connection strings. Each tenant database has its own Npgsql pool and background loops touch every
/// tenant, so idle connections must be released quickly; otherwise the pools together exhaust the server's connection limit.
/// </summary>
public static class TenantConnectionPool
{
    public const int MaxPoolSize = 10;

    public const int MinPoolSize = 0;

    public const int IdleLifetimeSeconds = 15;

    public const int PruningIntervalSeconds = 5;

    // Pooled connection string → tenant, so a tenant's pools in this process can be cleared by tenant id (Release).
    private static readonly ConcurrentDictionary<string, TenantId> Pools = new(StringComparer.Ordinal);

    /// <summary>Overrides the pool keys of a tenant connection string (also strings stored in the catalog before M11-00).</summary>
    public static string Apply(string connectionString) =>
        new NpgsqlConnectionStringBuilder(connectionString)
        {
            MaxPoolSize = MaxPoolSize,
            MinPoolSize = MinPoolSize,
            ConnectionIdleLifetime = IdleLifetimeSeconds,
            ConnectionPruningInterval = PruningIntervalSeconds,
        }.ConnectionString;

    /// <summary><see cref="Apply(string)"/> for a known tenant; remembers the pool so <see cref="Release"/> can clear it.</summary>
    public static string Apply(TenantId tenant, string connectionString)
    {
        var pooled = Apply(connectionString);
        Pools[pooled] = tenant;
        return pooled;
    }

    /// <summary>
    /// Closes the idle connections of the tenant's pools in this process now instead of after the idle lifetime; connections in
    /// use are closed when they are returned. Used by <see cref="TenantLoop"/> when a tenant's work ends.
    /// </summary>
    public static void Release(TenantId tenant)
    {
        foreach (var (pooled, owner) in Pools)
        {
            if (owner == tenant)
            {
                using var connection = new NpgsqlConnection(pooled);
                NpgsqlConnection.ClearPool(connection);
            }
        }
    }
}
