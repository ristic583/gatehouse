using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;

namespace Gatehouse.Application.Connectors;

public sealed record DispatchFulfillmentCommand(FulfillmentId FulfillmentId);
