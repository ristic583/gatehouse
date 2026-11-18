using Gatehouse.Application.LiveBuilding;
using Gatehouse.Application.LiveBuilding.Dtos;

namespace Gatehouse.Infrastructure.LiveBuilding;

/// <summary>Process-wide live update bus; <see cref="TenantLiveUpdatePublisher"/> binds publishing to the current tenant.</summary>
public interface ILiveUpdateBus : ILiveUpdateSubscriber
{
    public Task PublishAsync(string tenantId, LiveUpdateDto update, CancellationToken ct);
}
