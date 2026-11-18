using Gatehouse.Application.Audit;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Connectors.Messages;
using Gatehouse.Connector.Contracts.Events;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Fulfillment;

/// <summary>Consumer logic for a final <c>ConnectorCommandAcked</c>; the consumer saves.</summary>
public sealed class ApplyConnectorAckHandler(ITenantDb db, IAuditLog audit)
{
    public async Task<Result> HandleAsync(ConnectorCommandAcked message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (!message.IsFinal || message.FulfillmentId is not { } fid)
        {
            return Result.Success();
        }

        var f = await db.Fulfillments.FirstOrDefaultAsync(x => x.Id == fid, ct);
        if (f is null || f.Status is FulfillmentStatus.Confirmed or FulfillmentStatus.Superseded)
        {
            return Result.Success();
        }

        if (f.CommandId is { } current && current != message.CommandId)
        {
            // Late ack of an earlier attempt (the fulfillment was re-applied or retried since): only the current command settles it.
            return Result.Success();
        }

        var actor = AuditActor.ForAgent(message.ConnectorId);
        string action;
        Result result;
        if (message.Status == AckStatus.Applied)
        {
            result = f.ConfirmByAgent(message.ConnectorId.Value.ToString(), message.PacsRef, message.ReceivedAt);
            action = "fulfillment.confirmed";
        }
        else
        {
            result = f.Fail(
                message.Error?.Code ?? "connector.failed", message.Error?.Message
                    ?? "The connector reported a failure.", message.ReceivedAt);
            action = "fulfillment.failed";
        }

        if (!result.IsSuccess)
        {
            return Result.Success();
        }

        object payload = action == "fulfillment.confirmed"
            ? new
            {
                entitlementId = f.EntitlementId.Value,
                kind = f.Kind.ToString(),
                revision = f.Revision,
                confirmedAt = message.ReceivedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            }
            : new
            {
                entitlementId = f.EntitlementId.Value,
                kind = f.Kind.ToString(),
                revision = f.Revision,
                errorCode = message.Error?.Code,
                reason = message.Error?.Message,
            };
        audit.Record(actor, action, AuditSubjects.Fulfillment, f.Id.Value.ToString(), payload);
        return Result.Success();
    }
}
