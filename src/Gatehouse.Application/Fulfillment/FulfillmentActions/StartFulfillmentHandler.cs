using Gatehouse.Application.Fulfillment.Dtos;
using Gatehouse.Domain.Common;

namespace Gatehouse.Application.Fulfillment.FulfillmentActions;

public sealed class StartFulfillmentHandler(FulfillmentActionService service)
{
    public Task<Result<FulfillmentDetailDto>> HandleAsync(StartFulfillmentCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        return service.RunAsync(command.FulfillmentId, (f, now) => f.MarkPending(null, now), "fulfillment.started", null, ct);
    }
}
