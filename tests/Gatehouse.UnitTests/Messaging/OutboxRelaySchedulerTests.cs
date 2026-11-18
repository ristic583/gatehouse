using Gatehouse.Domain.Common;
using Gatehouse.Infrastructure.Messaging;
using Microsoft.Extensions.Time.Testing;

namespace Gatehouse.UnitTests.Messaging;

public sealed class OutboxRelaySchedulerTests
{
    private static readonly TimeSpan Sweep = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(250);

    private readonly FakeTimeProvider _time = new(DateTimeOffset.Parse("2026-11-16T10:00:00Z", System.Globalization.CultureInfo.InvariantCulture));
    private readonly FakeSignal _signal = new();
    private readonly List<TenantId> _tenants = [.. Enumerable.Range(0, 5).Select(i => TenantId.Create($"tenant-{i}").Value)];

    [Fact]
    public async Task Select_idle_tenants_after_first_sweep_visits_none()
    {
        var scheduler = new OutboxRelayScheduler(_signal, _time, Sweep);
        Assert.Equal(_tenants, await SelectAsync(scheduler));

        for (var i = 0; i < 10; i++)
        {
            _time.Advance(Tick);
            Assert.Empty(await SelectAsync(scheduler));
        }
    }

    [Fact]
    public async Task Select_signalled_tenant_visits_only_that_tenant()
    {
        var scheduler = await SweptAsync();
        _signal.Pending.Add(_tenants[2]);

        _time.Advance(Tick);
        Assert.Equal([_tenants[2]], await SelectAsync(scheduler));
        _time.Advance(Tick);
        Assert.Empty(await SelectAsync(scheduler));
    }

    [Fact]
    public async Task Select_after_sweep_interval_visits_every_tenant()
    {
        var scheduler = await SweptAsync();

        _time.Advance(Sweep);
        Assert.Equal(_tenants, await SelectAsync(scheduler));
    }

    [Fact]
    public async Task Select_without_signals_visits_every_tenant_each_tick()
    {
        var scheduler = await SweptAsync();
        _signal.Unavailable = true;

        _time.Advance(Tick);
        Assert.Equal(_tenants, await SelectAsync(scheduler));
    }

    [Fact]
    public async Task Select_tenant_with_rows_left_is_revisited_when_due()
    {
        var scheduler = await SweptAsync();
        scheduler.Record(_tenants[1], new OutboxRelayOutcome(100, _time.GetUtcNow()));
        scheduler.Record(_tenants[3], new OutboxRelayOutcome(0, _time.GetUtcNow() + TimeSpan.FromSeconds(2)));

        _time.Advance(Tick);
        Assert.Equal([_tenants[1]], await SelectAsync(scheduler));

        scheduler.Record(_tenants[1], new OutboxRelayOutcome(5, null));
        _time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal([_tenants[3]], await SelectAsync(scheduler));
    }

    [Fact]
    public async Task Select_failed_tenant_is_retried_after_failure_delay()
    {
        var scheduler = await SweptAsync();
        scheduler.RecordFailure(_tenants[4]);

        _time.Advance(Tick);
        Assert.Empty(await SelectAsync(scheduler));
        _time.Advance(OutboxRelayScheduler.FailureRetry);
        Assert.Equal([_tenants[4]], await SelectAsync(scheduler));
    }

    private async Task<OutboxRelayScheduler> SweptAsync()
    {
        var scheduler = new OutboxRelayScheduler(_signal, _time, Sweep);
        await SelectAsync(scheduler);
        return scheduler;
    }

    private Task<IReadOnlyList<TenantId>> SelectAsync(OutboxRelayScheduler scheduler) =>
        scheduler.SelectAsync(_tenants, TestContext.Current.CancellationToken);

    private sealed class FakeSignal : IOutboxSignal
    {
        public HashSet<TenantId> Pending { get; } = [];

        public bool Unavailable { get; set; }

        public void Notify(TenantId tenant) => Pending.Add(tenant);

        public Task<IReadOnlySet<TenantId>?> TakeAsync(IReadOnlyCollection<TenantId> tenants, CancellationToken ct)
        {
            if (Unavailable)
            {
                return Task.FromResult<IReadOnlySet<TenantId>?>(null);
            }

            var taken = tenants.Where(Pending.Contains).ToHashSet();
            Pending.ExceptWith(taken);
            return Task.FromResult<IReadOnlySet<TenantId>?>(taken);
        }
    }
}
