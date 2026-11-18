using StackExchange.Redis;
using Testcontainers.Redis;

[assembly: AssemblyFixture(typeof(Gatehouse.IntegrationTests.Infrastructure.RedisFixture))]

namespace Gatehouse.IntegrationTests.Infrastructure;

/// <summary>
/// One Redis container for the whole test assembly, reused across runs (<c>gatehouse-test-redis</c>, see <see cref="ReusedContainers"/>).
/// Tests never flush: each test uses its own tenant id (<see cref="UniqueTenantId"/>), so every key it writes
/// (<c>tenant:{id}:…</c>) is its own and classes run in parallel. Keys carry the stores' own TTLs and expire by themselves.
/// </summary>
public sealed class RedisFixture : IAsyncLifetime
{
    /// <summary>Change when the container configuration changes: a container with another key is replaced.</summary>
    private const string ReuseKey = "redis7-v1";

    private readonly RedisContainer _container = new RedisBuilder("redis:7.4")
        .Reuse("redis", ReuseKey)
        .Build();

    private ConnectionMultiplexer? _connection;

    public IConnectionMultiplexer Connection => _connection ?? throw new InvalidOperationException("Redis fixture is not initialized.");

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await ReusedContainers.RemoveStaleAsync("redis", ReuseKey);
        await _container.StartAsync();
        _connection = await ConnectionMultiplexer.ConnectAsync(ConnectionString);
    }

    public async ValueTask DisposeAsync()
    {
        // The container is not stopped: the next run reuses it. Remove it with `docker rm -f gatehouse-test-redis`.
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }

    /// <summary>A valid tenant slug that no other test uses, so the keys of one test never meet another's.</summary>
    public static string UniqueTenantId() => "t" + Guid.NewGuid().ToString("N")[..30];
}
