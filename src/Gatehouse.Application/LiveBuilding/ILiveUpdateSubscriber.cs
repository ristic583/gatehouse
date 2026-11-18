using Gatehouse.Application.LiveBuilding.Dtos;

namespace Gatehouse.Application.LiveBuilding;

/// <summary>Receives live updates of every tenant (pattern <c>tenant:*:live</c>); the handler gets the tenant id.</summary>
public interface ILiveUpdateSubscriber
{
    public Task SubscribeAsync(Func<string, LiveUpdateDto, Task> handler, CancellationToken ct);
}
