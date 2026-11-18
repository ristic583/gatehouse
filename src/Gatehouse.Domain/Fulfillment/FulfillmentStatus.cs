namespace Gatehouse.Domain.Fulfillment;

public enum FulfillmentStatus
{
    Desired,
    Pending,
    Confirmed,
    Failed,
    Drift,
    Superseded,
}
