using Gatehouse.Adapters.LegacyPacs;
using Gatehouse.Connector.Contracts.Adapters;
using Gatehouse.IntegrationTests.Infrastructure;
using LegacyPacs.Simulator;
using Npgsql;

namespace Gatehouse.IntegrationTests.Adapters;

public sealed class LegacyPacsAdapterTests(PostgresFixture postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset From = new(2026, 11, 9, 0, 0, 0, TimeSpan.Zero);

    private NpgsqlDataSource _db = null!;
    private string _conn = null!;
    private CancellationTokenSource _processor = null!;
    private LegacyPacsAdapter _adapter = null!;

    public async ValueTask InitializeAsync()
    {
        _conn = await postgres.CreateDatabaseAsync(PostgresFixture.UniqueName("legacypacsadapter"));
        _db = NpgsqlDataSource.Create(_conn);
        await LegacyPacsSchema.EnsureAsync(_db, CancellationToken.None);
        _adapter = Create(TimeSpan.FromSeconds(10));
        _processor = new CancellationTokenSource();
        var processor = new InboxProcessor(_db);
        _ = Task.Run(async () =>
        {
            while (!_processor.IsCancellationRequested)
            {
                try
                {
                    await processor.ProcessOnceAsync(_processor.Token);
                    await Task.Delay(200, _processor.Token);
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
        await _processor.CancelAsync();
        await _adapter.DisposeAsync();
        await _db.DisposeAsync();
    }

    private LegacyPacsAdapter Create(TimeSpan timeout) =>
        new(new LegacyPacsAdapterOptions(_conn, timeout, TimeSpan.FromMilliseconds(50)), new MemoryCursors());

    private static AccessGrant Grant(string level) => new("p1", "1234567890", level, From, From.AddDays(14));

    [Fact]
    public async Task Grant_is_applied_with_inbox_ref()
    {
        var r = await _adapter.GrantAccessAsync(Grant("AL-CPH-LAB"), TestContext.Current.CancellationToken);
        Assert.Equal(AdapterStatus.Applied, r.Status);
        Assert.StartsWith("inbox-row-", r.PacsRef, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Transient_error_is_retryable()
    {
        await using var cmd = _db.CreateCommand("UPDATE chaos_settings SET fail_next = 1 WHERE id = 1");
        await cmd.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        var r = await _adapter.GrantAccessAsync(Grant("AL-CPH-LAB"), TestContext.Current.CancellationToken);
        Assert.Equal(AdapterStatus.Failed, r.Status);
        Assert.Equal("legacypacs.transient", r.Error!.Code);
        Assert.True(r.Error.Retryable);
    }

    [Fact]
    public async Task Unprocessed_row_times_out_retryable()
    {
        await _processor.CancelAsync();
        await Task.Delay(400, TestContext.Current.CancellationToken);
        await using var quick = Create(TimeSpan.FromMilliseconds(500));
        var r = await quick.GrantAccessAsync(Grant("AL-CPH-LAB"), TestContext.Current.CancellationToken);
        Assert.Equal("legacypacs.timeout", r.Error!.Code);
        Assert.True(r.Error.Retryable);
    }

    [Fact]
    public async Task Unknown_level_is_not_retryable()
    {
        var r = await _adapter.GrantAccessAsync(Grant("AL-NOPE"), TestContext.Current.CancellationToken);
        Assert.Equal("legacypacs.unknown_access_level", r.Error!.Code);
        Assert.False(r.Error.Retryable);
    }

    [Fact]
    public async Task Set_pin_is_unsupported()
    {
        var r = await _adapter.SetPinAsync(new PinWrite("p1", "1234567890", new SensitivePin("1234")), TestContext.Current.CancellationToken);
        Assert.Equal(AdapterStatus.Unsupported, r.Status);
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
