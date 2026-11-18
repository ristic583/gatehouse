using System.Collections.Concurrent;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;

namespace Gatehouse.Application.Connectors;

public sealed class InMemoryConnectorPollGate : IConnectorPollGate
{
    private readonly ConcurrentDictionary<string, byte> _open = new();

    public Task<IAsyncDisposable?> TryAcquireAsync(TenantId tenantId, PacsInstanceId pacsInstanceId, TimeSpan ttl, CancellationToken ct)
    {
        var key = $"{tenantId.Value}:{pacsInstanceId.Value}";
        IAsyncDisposable? lease = _open.TryAdd(key, 0) ? new Lease(_open, key) : null;
        return Task.FromResult(lease);
    }

    private sealed class Lease(ConcurrentDictionary<string, byte> open, string key) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            open.TryRemove(key, out _);
            return ValueTask.CompletedTask;
        }
    }
}
