using Gatehouse.Adapters.LegacyPacs;
using Gatehouse.Connector.Contracts.Adapters;
using Gatehouse.Connector.Contracts.Events;
using Gatehouse.ContractTests.Conformance;
using LegacyPacs.Simulator;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Gatehouse.ContractTests.LegacyPacs;

public sealed class LegacyPacsFixture : IPacsAdapterFixture
{
    /// <summary>Change when the container configuration changes (and remove the old container: <c>docker rm -f gatehouse-test-contract-postgres</c>).</summary>
    private const string ReuseKey = "postgres17-v1";
    private const string DatabasePrefix = "legacypacs_";
    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(3);

    // Reused across runs (kept running); every fixture gets its own database, dropped on dispose.
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17")
        .WithName("gatehouse-test-contract-postgres")
        .WithLabel("gatehouse-test", "contract-postgres")
        .WithLabel("gatehouse-test.key", ReuseKey)
        .WithReuse(_ => "contract-postgres:" + ReuseKey)
        .Build();

    private readonly string _database = $"{DatabasePrefix}{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}_{Guid.NewGuid():N}"[..48];
    private readonly CancellationTokenSource _stop = new();
    private string _connectionString = string.Empty;
    private NpgsqlDataSource _db = null!;
    private LegacyPacsAdapter _adapter = null!;

    public IPacsAdapter Adapter => _adapter;

    public string KnownAccessLevelRef => "AL-CPH-LAB";

    public string KnownPacsDoorRef => "D-CPH-LAB";

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
        await using (var server = NpgsqlDataSource.Create(_container.GetConnectionString()))
        {
            await DropStaleDatabasesAsync(server);
            await using var create = server.CreateCommand($"CREATE DATABASE \"{_database}\"");
            await create.ExecuteNonQueryAsync();
        }

        _connectionString = new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = _database }.ConnectionString;
        _db = NpgsqlDataSource.Create(_connectionString);
        await LegacyPacsSchema.EnsureAsync(_db, CancellationToken.None);
        _adapter = new LegacyPacsAdapter(
            new LegacyPacsAdapterOptions(_connectionString, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(50)),
            new MemoryCursors());
        var processor = new InboxProcessor(_db);
        _ = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    await processor.ProcessOnceAsync(_stop.Token);
                    await Task.Delay(100, _stop.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        });
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _adapter.DisposeAsync();
        await _db.DisposeAsync();
        await using var server = NpgsqlDataSource.Create(_container.GetConnectionString());
        await using var drop = server.CreateCommand($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)");
        await drop.ExecuteNonQueryAsync();
    }

    public async Task InjectTransientFailureAsync(int count)
    {
        await using var cmd = _db.CreateCommand("UPDATE chaos_settings SET fail_next = $1 WHERE id = 1");
        cmd.Parameters.AddWithValue(count);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task EmitDoorEventAsync(string pacsDoorRef, DoorEventType type, string? cardNumber)
    {
        await using var cmd = _db.CreateCommand("INSERT INTO event_log (occurred_at, type, door_ref, card_number) VALUES (now(), $1, $2, $3)");
        cmd.Parameters.AddWithValue(type.ToString());
        cmd.Parameters.AddWithValue(pacsDoorRef);
        cmd.Parameters.AddWithValue((object?)cardNumber ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Databases of crashed runs: the name carries its creation time (unix seconds).</summary>
    private static async Task DropStaleDatabasesAsync(NpgsqlDataSource server)
    {
        var stale = new List<string>();
        await using (var select = server.CreateCommand($"SELECT datname FROM pg_database WHERE datname LIKE '{DatabasePrefix}%'"))
        await using (var reader = await select.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(0);
                var created = name[DatabasePrefix.Length..].Split('_')[0];
                if (!long.TryParse(created, out var seconds) || DateTimeOffset.FromUnixTimeSeconds(seconds) < DateTimeOffset.UtcNow - StaleAfter)
                {
                    stale.Add(name);
                }
            }
        }

        foreach (var name in stale)
        {
            await using var drop = server.CreateCommand($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)");
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed class MemoryCursors : IAdapterCursorStore
    {
        private readonly Dictionary<string, string> _values = [];

        public ValueTask<string?> GetAsync(string name, CancellationToken ct) => ValueTask.FromResult(_values.GetValueOrDefault(name));

        public ValueTask SetAsync(string name, string value, CancellationToken ct)
        {
            _values[name] = value;
            return ValueTask.CompletedTask;
        }
    }
}
