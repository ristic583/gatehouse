using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Sites.Dtos;
using Gatehouse.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Sites.ListSecurityLevels;

/// <summary><c>GET /api/v1/admin/security-levels</c>: read-only, ordered by rank.</summary>
public sealed class ListSecurityLevelsHandler(ITenantDb db)
{
    public async Task<Result<IReadOnlyList<SecurityLevelAdminDto>>> HandleAsync(ListSecurityLevelsQuery query, CancellationToken ct)
    {
        IReadOnlyList<SecurityLevelAdminDto> levels = await db.SecurityLevels.AsNoTracking().OrderBy(l => l.Rank)
            .Select(l => new SecurityLevelAdminDto(l.Id.Value, l.Name, l.Rank, l.ApproverChain, l.MaxDurationDays))
            .ToListAsync(ct);
        return Result.Success(levels);
    }
}
