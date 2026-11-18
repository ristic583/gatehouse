using System.Collections.Immutable;
using Gatehouse.Application.LiveBuilding.Dtos;

namespace Gatehouse.Infrastructure.LiveBuilding;

/// <summary>Single-process bus (tests, hosts without Redis).</summary>
public sealed class InMemoryLiveUpdateBus : ILiveUpdateBus
{
    private ImmutableList<Func<string, LiveUpdateDto, Task>> _handlers = [];

    public async Task PublishAsync(string tenantId, LiveUpdateDto update, CancellationToken ct)
    {
        foreach (var handler in _handlers)
        {
            await handler(tenantId, update);
        }
    }

    public Task SubscribeAsync(Func<string, LiveUpdateDto, Task> handler, CancellationToken ct)
    {
        ImmutableInterlocked.Update(ref _handlers, list => list.Add(handler));
        return Task.CompletedTask;
    }
}
