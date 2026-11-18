namespace Gatehouse.Domain.Access;

public enum AreaAccessReason
{
    AreaNotMapped,
    ConnectorOffline,
    Confirmed,
    RevocationPending,
    AwaitingApproval,
    AwaitingFulfillment,
    ScheduledStart,
    OutsideWindow,
    NoEntitlement,
}
