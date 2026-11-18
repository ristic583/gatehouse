using Gatehouse.Application.Fulfillment.Dtos;
using Gatehouse.Domain.Common;

namespace Gatehouse.Application.Fulfillment.FulfillmentActions;

public sealed class FailFulfillmentHandler(FulfillmentActionService service)
{
    public Task<Result<FulfillmentDetailDto>> HandleAsync(FailFulfillmentCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var reason = command.Reason?.Trim() ?? string.Empty;

        return service.RunAsync(
            command.FulfillmentId,
            (f, now) => f.Fail("staff.manual_failure", reason, now),
            "fulfillment.failed",
            null,
            ct);
    }
}
