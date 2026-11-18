using Gatehouse.Domain.Fulfillment;

namespace Gatehouse.Application.Connectors;

public sealed record SupersedeFulfillmentCommandsCommand(FulfillmentId FulfillmentId);
