namespace Gatehouse.Domain.Access;

public static class AreaAccessStateCalculator
{
    public static AreaAccessResult Calculate(AreaAccessInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var now = input.Now;
        var list = input.Entitlements;

        if (!input.AreaMapped)
        {
            return new(AreaAccessState.Unknown, AreaAccessReason.AreaNotMapped, false, null);
        }

        if (input.AnyConnectorOffline)
        {
            return new(AreaAccessState.Unknown, AreaAccessReason.ConnectorOffline, false, input.OfflineSince);
        }

        var hasAccess = input.PersonActive
            && input.Eligibility.Contains(now)
            && list.Any(e => e.Status == EntitlementStatus.Approved && e.AllGrantsConfirmed && e.RequestedStart <= now
            && now < e.EffectiveEnd);
        if (hasAccess)
        {
            return new(AreaAccessState.HasAccess, AreaAccessReason.Confirmed, input.HasActiveCard == false, null);
        }

        if (list.Any(e => e.Status == EntitlementStatus.Revoked && e.RevokePendingConfirmation))
        {
            return Pending(AreaAccessReason.RevocationPending);
        }

        if (list.Any(e => e.Status == EntitlementStatus.PendingApproval))
        {
            return Pending(AreaAccessReason.AwaitingApproval);
        }

        if (list.Any(e => e.Status == EntitlementStatus.Approved && !e.AllGrantsConfirmed && e.EffectiveEnd > now))
        {
            return Pending(AreaAccessReason.AwaitingFulfillment);
        }

        if (list.Any(e => e.Status == EntitlementStatus.Approved && e.AllGrantsConfirmed && now < e.RequestedStart))
        {
            return Pending(AreaAccessReason.ScheduledStart);
        }

        if (list.Any(e => e.Status == EntitlementStatus.Approved && e.AllGrantsConfirmed))
        {
            return new(AreaAccessState.NoAccess, AreaAccessReason.OutsideWindow, false, null);
        }

        return new(AreaAccessState.NoAccess, AreaAccessReason.NoEntitlement, false, null);
    }

    private static AreaAccessResult Pending(AreaAccessReason reason) => new(AreaAccessState.Pending, reason, false, null);
}
