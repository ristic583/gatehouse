using Gatehouse.Application.LiveBuilding;
using Microsoft.AspNetCore.SignalR;

namespace Gatehouse.Api.Features.LiveBuilding;

/// <summary>Forwards live updates (Redis channel <c>tenant:{tenantId}:live</c> or in-memory bus) to the matching SignalR site
/// group.</summary>
public sealed class LiveUpdateRelay(ILiveUpdateSubscriber subscriber, IHubContext<LiveBuildingHub> hub) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        subscriber.SubscribeAsync(
            (tenant, update) =>
                hub.Clients.Group(LiveBuildingHub.SiteGroup(tenant, update.SiteId))
                    .SendAsync(LiveBuildingHub.LiveMethod, update, stoppingToken),
            stoppingToken);
}
