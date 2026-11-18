using Gatehouse.Application.LiveBuilding.Dtos;

namespace Gatehouse.Application.LiveBuilding;

/// <summary>Publishes to channel <c>tenant:{tenantId}:live</c> of the current tenant.</summary>
public interface ILiveUpdatePublisher
{
    public Task PublishAsync(LiveUpdateDto update, CancellationToken ct);
}
