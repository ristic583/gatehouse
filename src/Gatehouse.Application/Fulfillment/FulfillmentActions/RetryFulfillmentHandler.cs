using Gatehouse.Application.Fulfillment.Dtos;
using Gatehouse.Domain.Common;

namespace Gatehouse.Application.Fulfillment.FulfillmentActions;

public sealed class RetryFulfillmentHandler(FulfillmentActionService service)
{
    public Task<Result<FulfillmentDetailDto>> HandleAsync(RetryFulfillmentCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        return service.RunAsync(command.FulfillmentId, (f, now) => f.Retry(now), "fulfillment.retried", null, ct, staffManaged: false);
    }
}
