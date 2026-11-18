using Gatehouse.Application.Fulfillment.Dtos;
using Gatehouse.Application.Fulfillment.PacsAdmin;
using Gatehouse.Domain.Common;

namespace Gatehouse.Application.Fulfillment.FulfillmentActions;

public sealed class ConfirmFulfillmentHandler(
    FulfillmentActionService service,
    Gatehouse.Application.Identity.ICurrentUser currentUser,
    GrantMigrationService migration)
{
    public async Task<Result<FulfillmentDetailDto>> HandleAsync(ConfirmFulfillmentCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var me = await currentUser.RequirePersonIdAsync(ct);
        return await service.RunAsync(
            command.FulfillmentId,
            (f, now) => f.ConfirmByStaff(me, now),
            "fulfillment.confirmed",
            // Manual instance: a migration revoke is out once staff confirm it; only then is the new level planned (GH-ACCESS-01).
            async context => await migration.PlanFollowUpGrantAsync(context.Fulfillment, context.Actor, context.Now, ct),
            ct);
    }
}
