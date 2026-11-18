using Gatehouse.Application.Common.Persistence;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Sites.Admin;

/// <summary>Site name and code are unique per tenant (create and update).</summary>
public static class SiteUniqueness
{
    public static async Task<Result> CheckAsync(ITenantDb db, string name, string code, SiteId? except, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (await db.Sites.AnyAsync(s => s.Name == name && s.Id != except, ct))
        {
            return Error.Conflict("site.name_taken", "Another site has this name.");
        }

        if (await db.Sites.AnyAsync(s => s.Code == code && s.Id != except, ct))
        {
            return Error.Conflict("site.code_taken", "Another site has this code.");
        }

        return Result.Success();
    }
}
