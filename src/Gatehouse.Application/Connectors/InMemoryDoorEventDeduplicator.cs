using System.Collections.Concurrent;
using Gatehouse.Domain.Common;

namespace Gatehouse.Application.Connectors;

public sealed class InMemoryDoorEventDeduplicator(TimeProvider time) : IDoorEventDeduplicator
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _seen = new();

    public Task<bool> TryClaimAsync(TenantId tenantId, Guid eventId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var key = Key(tenantId, eventId);
        if (_seen.TryGetValue(key, out var expires) && expires > now)
        {
            return Task.FromResult(false);
        }

        _seen[key] = now + IDoorEventDeduplicator.Window;
        return Task.FromResult(true);
    }

    public Task ReleaseAsync(TenantId tenantId, IReadOnlyCollection<Guid> eventIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(eventIds);
        foreach (var id in eventIds)
        {
            _seen.TryRemove(Key(tenantId, id), out _);
        }

        return Task.CompletedTask;
    }

    private static string Key(TenantId tenantId, Guid eventId) => $"{tenantId.Value}:{eventId}";
}
