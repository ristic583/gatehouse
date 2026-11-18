using System.Collections.Concurrent;
using Gatehouse.Domain.Common;

namespace Gatehouse.Infrastructure.Messaging;

/// <summary>
/// Picks the tenants the outbox relay visits on a tick (M11): tenants signalled by <see cref="IOutboxSignal"/>, tenants whose remaining
/// rows are due (full batch, publish backoff, failed visit), and every tenant once per sweep interval in case a signal was lost.
/// Idle tenants are therefore not reconnected on every tick, while <see cref="Tenancy.TenantLoop"/> still releases each visited pool.
/// </summary>
public sealed class OutboxRelayScheduler(IOutboxSignal signal, TimeProvider time, TimeSpan sweepInterval)
{
    public const string SweepIntervalSetting = "Outbox:SweepIntervalMs";

    public const int DefaultSweepIntervalMs = 10_000;

    /// <summary>A visit that failed (database unreachable) is retried after this delay instead of waiting for the sweep.</summary>
    public static readonly TimeSpan FailureRetry = TimeSpan.FromSeconds(1);

    private readonly ConcurrentDictionary<TenantId, DateTimeOffset> _due = new();

    private DateTimeOffset? _lastSweep;

    public async Task<IReadOnlyList<TenantId>> SelectAsync(IReadOnlyList<TenantId> active, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(active);
        var now = time.GetUtcNow();
        foreach (var stale in _due.Keys.Except(active).ToList())
        {
            _due.TryRemove(stale, out _);
        }

        // Taken also on a sweep, so the signals the sweep answers do not cause a second visit.
        var signalled = await signal.TakeAsync(active, ct);
        if (signalled is null || _lastSweep is null || now - _lastSweep >= sweepInterval)
        {
            if (signalled is not null)
            {
                _lastSweep = now;
            }

            return active;
        }

        return active.Where(t => signalled.Contains(t) || (_due.TryGetValue(t, out var at) && at <= now)).ToList();
    }

    /// <summary>Records a visit's outcome: the tenant is revisited when its earliest pending row is due.</summary>
    public void Record(TenantId tenant, OutboxRelayOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (outcome.NextAttemptAt is { } next)
        {
            _due[tenant] = next;
        }
        else
        {
            _due.TryRemove(tenant, out _);
        }
    }

    public void RecordFailure(TenantId tenant) => _due[tenant] = time.GetUtcNow() + FailureRetry;
}
