using Gatehouse.Domain.Fulfillment;

namespace Gatehouse.Application.Tasks.FulfillmentFollowUps;

public sealed record OpenFulfillmentFailedTaskCommand(FulfillmentId FulfillmentId, DateTimeOffset Now);
