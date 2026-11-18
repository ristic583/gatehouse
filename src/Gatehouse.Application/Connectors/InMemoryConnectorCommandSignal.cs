using System.Collections.Concurrent;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;

namespace Gatehouse.Application.Connectors;

public sealed class InMemoryConnectorCommandSignal : IConnectorCommandSignal
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _waiters = new();

    public Task NotifyAsync(TenantId tenantId, PacsInstanceId pacsInstanceId, CancellationToken ct)
    {
        if (_waiters.TryRemove(Key(tenantId, pacsInstanceId), out var waiter))
        {
            waiter.TrySetResult();
        }

        return Task.CompletedTask;
    }

    public async Task WaitAsync(TenantId tenantId, PacsInstanceId pacsInstanceId, TimeSpan timeout, CancellationToken ct)
    {
        var waiter = _waiters.GetOrAdd(
            Key(tenantId, pacsInstanceId), _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        try
        {
            await waiter.Task.WaitAsync(timeout, ct);
        }
        catch (TimeoutException)
        {
            // Nothing queued within the window.
        }
    }

    private static string Key(TenantId tenantId, PacsInstanceId id) => $"{tenantId.Value}:{id.Value}";
}
