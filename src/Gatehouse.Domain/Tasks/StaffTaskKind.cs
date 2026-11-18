namespace Gatehouse.Domain.Tasks;

public enum StaffTaskKind
{
    FulfillmentFailed,
    RevocationUnconfirmed,
    ReconciliationFinding,
    DriftAlert,
    PinReveal,
    ScheduledRemovalRefused,
}
