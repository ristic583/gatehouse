namespace Gatehouse.Domain.Fulfillment;

public readonly record struct FulfillmentId(Guid Value)
{
    public static FulfillmentId New() => new(Guid.CreateVersion7());
}
