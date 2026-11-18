namespace CloudPacs.Simulator;

public sealed class WebhookRegistry
{
    private readonly Lock _gate = new();
    private readonly List<WebhookSubscription> _subscriptions = [];

    public WebhookSubscription Register(string url, string secret)
    {
        lock (_gate)
        {
            var index = _subscriptions.FindIndex(s => s.Url == url);
            if (index >= 0)
            {
                _subscriptions[index] = _subscriptions[index] with { Secret = secret };
                return _subscriptions[index];
            }

            var subscription = new WebhookSubscription(Guid.CreateVersion7(), url, secret);
            _subscriptions.Add(subscription);
            return subscription;
        }
    }

    public bool Remove(Guid id)
    {
        lock (_gate)
        {
            return _subscriptions.RemoveAll(s => s.Id == id) > 0;
        }
    }

    public IReadOnlyList<WebhookSubscription> Snapshot()
    {
        lock (_gate)
        {
            return [.. _subscriptions];
        }
    }
}
