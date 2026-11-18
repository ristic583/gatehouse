using System.Collections.Concurrent;

namespace Gatehouse.Api.Features.LiveBuilding;

/// <summary>Site groups joined by this process's hub connections, so <see cref="LiveSubscriptionRevalidator"/> can re-check them.</summary>
public sealed class LiveSubscriptionRegistry
{
    private readonly ConcurrentDictionary<(string ConnectionId, Guid SiteId), LiveSubscription> _subscriptions = new();

    public void Track(LiveSubscription subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        _subscriptions[(subscription.ConnectionId, subscription.SiteId)] = subscription;
    }

    public void Untrack(string connectionId, Guid siteId) => _subscriptions.TryRemove((connectionId, siteId), out _);

    public void RemoveConnection(string connectionId)
    {
        foreach (var key in _subscriptions.Keys.Where(k => k.ConnectionId == connectionId))
        {
            _subscriptions.TryRemove(key, out _);
        }
    }

    public IReadOnlyList<LiveSubscription> Snapshot() => [.. _subscriptions.Values];
}
