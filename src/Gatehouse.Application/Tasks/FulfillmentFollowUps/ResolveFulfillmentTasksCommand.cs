using Gatehouse.Domain.Fulfillment;

namespace Gatehouse.Application.Tasks.FulfillmentFollowUps;

public sealed record ResolveFulfillmentTasksCommand(
    FulfillmentId FulfillmentId, ConfirmerKind ConfirmedByKind, string ConfirmedById, DateTimeOffset Now);
