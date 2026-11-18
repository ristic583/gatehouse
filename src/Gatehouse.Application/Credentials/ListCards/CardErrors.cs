using Gatehouse.Application.Common.Persistence;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Credentials.ListCards;

internal static class CardErrors
{
    public static Error NotFound() => Error.NotFound("card.not_found", "Card not found.");

    public static async Task<IReadOnlyList<PacsInstanceId>> InstancesOfAsync(
        ITenantDb db, IReadOnlyList<CommandId> commandIds, CancellationToken ct)
    {
        if (commandIds.Count == 0)
        {
            return [];
        }

        var ids = commandIds.ToList();
        return await db.ConnectorCommands.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .Select(c => c.PacsInstanceId)
            .Distinct()
            .ToListAsync(ct);
    }
}
