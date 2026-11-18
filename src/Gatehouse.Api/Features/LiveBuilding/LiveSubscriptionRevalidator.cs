using Microsoft.AspNetCore.SignalR;

namespace Gatehouse.Api.Features.LiveBuilding;

/// <summary>
/// Re-checks every live site membership of this process every <see cref="Interval"/> and removes connections whose user may no longer
/// view the site (role or scope removed, person deactivated): a joined feed never outlives authorization by more than the interval
/// (GH-AUTH-02).
/// </summary>
public sealed partial class LiveSubscriptionRevalidator(
    LiveSubscriptionRegistry registry,
    ILiveSiteAccessChecker access,
    IHubContext<LiveBuildingHub> hub,
    TimeProvider time,
    ILogger<LiveSubscriptionRevalidator> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Hub client method sent to a connection before its membership is removed: <c>SubscriptionRevoked(siteId, code)</c>.
    /// </summary>
    public const string RevokedMethod = "SubscriptionRevoked";

    /// <summary>Code carried by <see cref="RevokedMethod"/>.</summary>
    public const string RevokedCode = "auth.forbidden";

    /// <summary>One pass; returns how many memberships were removed.</summary>
    public async Task<int> RevalidateOnceAsync(CancellationToken ct)
    {
        var removed = 0;
        foreach (var s in registry.Snapshot())
        {
            bool allowed;
            try
            {
                allowed = await access.CanViewAsync(s.Tenant, s.Subject, s.Email, s.SiteId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // host boundary: a failing check (e.g. tenant suspended) removes the membership rather than keeping it
                LogCheckFailed(s.SiteId, ex.GetType().Name);
                allowed = false;
            }

            if (allowed)
            {
                continue;
            }

            try
            {
                // Tell the client first so it shows "not permitted" instead of a silently frozen view.
                await hub.Clients.Client(s.ConnectionId).SendAsync(RevokedMethod, s.SiteId, RevokedCode, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // host boundary: a failed notice must never keep the membership
                LogNoticeFailed(s.SiteId, ex.GetType().Name);
            }

            await hub.Groups.RemoveFromGroupAsync(s.ConnectionId, LiveBuildingHub.SiteGroup(s.Tenant, s.SiteId), ct);
            registry.Untrack(s.ConnectionId, s.SiteId);
            LogRemoved(s.SiteId);
            removed++;
        }

        return removed;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(Interval, time, stoppingToken);
                await RevalidateOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Live feed membership for site {SiteId} removed: access no longer allowed")]
    private partial void LogRemoved(Guid siteId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Live feed revoked notice for site {SiteId} failed ({Reason}); removed anyway")]
    private partial void LogNoticeFailed(Guid siteId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Live feed access check for site {SiteId} failed ({Reason}); membership removed")]
    private partial void LogCheckFailed(Guid siteId, string reason);
}
