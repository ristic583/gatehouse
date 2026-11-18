using Gatehouse.Application.Common.Persistence;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Connectors;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Connectors;

/// <summary>Supersedes still-queued connector commands of a superseded fulfillment (idempotent); the consumer saves.</summary>
public sealed class SupersedeFulfillmentCommandsHandler(ITenantDb db, TimeProvider time)
{
    public async Task<Result> HandleAsync(SupersedeFulfillmentCommandsCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var now = time.GetUtcNow();
        var queued = await db.ConnectorCommands
            .Where(c => c.FulfillmentId == command.FulfillmentId && c.Status == ConnectorCommandStatus.Queued)
            .ToListAsync(ct);
        foreach (var c in queued)
        {
            c.Supersede(now);
        }

        return Result.Success();
    }
}
