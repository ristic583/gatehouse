using System.Collections.Concurrent;
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(Gatehouse.IntegrationTests.Infrastructure.PostgresFixture))]

namespace Gatehouse.IntegrationTests.Infrastructure;

/// <summary>
/// One Postgres container for the whole test assembly, reused across runs (<c>gatehouse-test-postgres</c>, see <see cref="ReusedContainers"/>).
/// Every database a test uses is unique (<see cref="UniqueName"/>) or cloned from a template (<see cref="CloneDatabaseAsync"/>),
/// so test classes run in parallel without sharing data. Databases are dropped by their owner or at the end of the run;
/// those left by a crashed run are dropped by a later run once they are older than <see cref="StaleAfter"/>.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    /// <summary>Change when the container configuration changes: a container with another key is replaced.</summary>
    private const string ReuseKey = "postgres17-v1";

    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(3);

    // Many WebApplicationFactory hosts (each with its own Npgsql pools) run in parallel; durability is not needed for tests.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17")
        .Reuse("postgres", ReuseKey)
        .WithCommand("-c", "max_connections=500", "-c", "fsync=off", "-c", "synchronous_commit=off", "-c", "full_page_writes=off")
        .Build();

    private readonly ConcurrentDictionary<string, byte> _databases = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Lazy<Task<object>>> _templates = new(StringComparer.Ordinal);

    public string ServerConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await ReusedContainers.RemoveStaleAsync("postgres", ReuseKey);
        await _container.StartAsync();
        await DropStaleDatabasesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Parallel.ForEachAsync(_databases.Keys.ToArray(), new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (name, ct) => await DropDatabaseAsync(name, ct));

        // The container is not stopped: the next run (or a concurrent one from another checkout) reuses it.
        // Remove it with `docker rm -f gatehouse-test-postgres`.
    }

    /// <summary>Creates an empty database (unique name per call is the caller's job) and returns its connection string.</summary>
    public async Task<string> CreateDatabaseAsync(string name, CancellationToken ct = default)
    {
        await TrackAsync(name, ct);
        await ExecuteAsync($"CREATE DATABASE \"{name}\"", ct);
        return ConnectionStringFor(name);
    }

    /// <summary>Creates <paramref name="name"/> as a copy of <paramref name="template"/> (which must have no open connections).</summary>
    public async Task CloneDatabaseAsync(string template, string name, CancellationToken ct = default)
    {
        await TrackAsync(name, ct);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await ExecuteAsync($"CREATE DATABASE \"{name}\" TEMPLATE \"{template}\"", ct);
                return;
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ObjectInUse && attempt < 50)
            {
                await Task.Delay(100, ct); // a concurrent clone of the same template held it briefly
            }
        }
    }

    /// <summary>Drops a database created through this fixture, closing any connections to it.</summary>
    public async Task DropDatabaseAsync(string name, CancellationToken ct = default)
    {
        await ExecuteAsync($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)", ct);
        await ExecuteAsync("DELETE FROM gatehouse_test_databases WHERE name = @name", ct, ("name", name));
        _databases.TryRemove(name, out _);
    }

    /// <summary>Closes every connection to the given databases (e.g. before they are used as templates).</summary>
    public Task TerminateConnectionsAsync(IEnumerable<string> names, CancellationToken ct = default) =>
        ExecuteAsync("SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = ANY(@names) AND pid <> pg_backend_pid()", ct, ("names", names.ToArray()));

    /// <summary>Connection string for a database on this server; unpooled unless <paramref name="maxPoolSize"/> is given.</summary>
    public string ConnectionStringFor(string database, int? maxPoolSize = null)
    {
        var builder = new NpgsqlConnectionStringBuilder(ServerConnectionString) { Database = database, Pooling = maxPoolSize is not null };
        if (maxPoolSize is { } size)
        {
            builder.MaxPoolSize = size;
        }

        return builder.ConnectionString;
    }

    /// <summary>Builds a template once per run (per <paramref name="key"/>) and returns it to every caller.</summary>
    public async Task<T> GetOrCreateTemplateAsync<T>(string key, Func<Task<T>> build)
        where T : class
    {
        var lazy = _templates.GetOrAdd(key, _ => new Lazy<Task<object>>(async () => await build()));
        return (T)await lazy.Value;
    }

    public static string UniqueName(string prefix) => $"{prefix}_{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 13, 63)];

    private async Task TrackAsync(string name, CancellationToken ct)
    {
        _databases.TryAdd(name, 0);
        await ExecuteAsync("INSERT INTO gatehouse_test_databases (name) VALUES (@name) ON CONFLICT DO NOTHING", ct, ("name", name));
    }

    /// <summary>
    /// The container is shared by runs (also concurrent ones from other checkouts): adopt every database not yet tracked
    /// and drop those tracked for longer than <see cref="StaleAfter"/>.
    /// </summary>
    private async Task DropStaleDatabasesAsync()
    {
        try
        {
            await ExecuteAsync("CREATE TABLE IF NOT EXISTS gatehouse_test_databases (name text PRIMARY KEY, created_at timestamptz NOT NULL DEFAULT now())", CancellationToken.None);
        }
        catch (PostgresException ex) when (ex.SqlState is PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.DuplicateTable)
        {
            // Created concurrently by another run.
        }

        await ExecuteAsync(
            "INSERT INTO gatehouse_test_databases (name) SELECT datname FROM pg_database WHERE NOT datistemplate AND datname <> 'postgres' ON CONFLICT DO NOTHING",
            CancellationToken.None);

        var stale = new List<string>();
        await using (var connection = new NpgsqlConnection(ServerConnectionString))
        {
            await connection.OpenAsync();
            await using var select = connection.CreateCommand();
            select.CommandText = "SELECT name FROM gatehouse_test_databases WHERE created_at < now() - @age";
            select.Parameters.AddWithValue("age", StaleAfter);
            await using var reader = await select.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                stale.Add(reader.GetString(0));
            }
        }

        await Parallel.ForEachAsync(stale, new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (name, ct) => await DropDatabaseAsync(name, ct));
    }

    private async Task ExecuteAsync(string sql, CancellationToken ct, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(ServerConnectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Database names come from UniqueName/templates of this fixture; CREATE/DROP DATABASE take no parameters.
        command.CommandText = sql;
#pragma warning restore CA2100
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(ct);
    }
}
