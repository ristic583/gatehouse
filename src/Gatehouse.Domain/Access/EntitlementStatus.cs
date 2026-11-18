namespace Gatehouse.Domain.Access;

public enum EntitlementStatus
{
    PendingApproval,
    Approved,
    Rejected,
    Cancelled,
    Revoked,
    Expired,
}
