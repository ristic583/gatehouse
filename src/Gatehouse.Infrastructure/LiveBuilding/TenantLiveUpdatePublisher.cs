using Gatehouse.Application.LiveBuilding;
using Gatehouse.Application.LiveBuilding.Dtos;
using Gatehouse.Application.Tenancy;

namespace Gatehouse.Infrastructure.LiveBuilding;

public sealed class TenantLiveUpdatePublisher(ILiveUpdateBus bus, ITenantContext tenant) : ILiveUpdatePublisher
{
    public Task PublishAsync(LiveUpdateDto update, CancellationToken ct) => bus.PublishAsync(tenant.TenantId.Value, update, ct);
}
