namespace Gatehouse.Application.Fulfillment.FulfillmentActions;

public sealed record FailFulfillmentCommand(Guid FulfillmentId, string? Reason);
