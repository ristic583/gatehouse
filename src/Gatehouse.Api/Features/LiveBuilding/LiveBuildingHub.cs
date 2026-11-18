using Gatehouse.Api.Tenancy;
using Microsoft.AspNetCore.SignalR;

namespace Gatehouse.Api.Features.LiveBuilding;

/// <summary>
/// Live building hub (authorization.md §9): clients join per-site groups <c>tenant:{tenantId}:site:{siteId}</c> and receive <c>live</c>
/// messages. Memberships are re-checked by <see cref="LiveSubscriptionRevalidator"/> (GH-AUTH-02).
/// </summary>
public sealed class LiveBuildingHub(ILiveSiteAccessChecker access, LiveSubscriptionRegistry registry) : Hub
{
    public const string Path = "/api/v1/live/hub";
    public const string LiveMethod = "live";

    public static string SiteGroup(string tenantId, Guid siteId) => $"tenant:{tenantId}:site:{siteId}";

    public async Task JoinSite(Guid siteId)
    {
        var tenant = TenantClaim();
        var subject = Context.User?.FindFirst("sub")?.Value ?? string.Empty;
        var email = Context.User?.FindFirst("email")?.Value;
        if (!await access.CanViewAsync(tenant, subject, email, siteId, Context.ConnectionAborted))
        {
            throw new HubException("auth.forbidden");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, SiteGroup(tenant, siteId), Context.ConnectionAborted);
        registry.Track(new LiveSubscription(Context.ConnectionId, tenant, subject, email, siteId));
    }

    public async Task LeaveSite(Guid siteId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, SiteGroup(TenantClaim(), siteId), Context.ConnectionAborted);
        registry.Untrack(Context.ConnectionId, siteId);
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        registry.RemoveConnection(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    private string TenantClaim() =>
        Context.User?.FindFirst(TenantResolutionMiddleware.TenantClaim)?.Value ?? throw new HubException("auth.forbidden");
}
