using Gatehouse.Application.Audit.Dtos;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Audit.ListAuditEntries;

public sealed class ListAuditEntriesHandler(ITenantDb db)
{
    public async Task<Result<IReadOnlyList<AuditEntryDto>>> HandleAsync(ListAuditEntriesQuery query, CancellationToken ct)
    {
        var limit = Math.Clamp(query.Limit, 1, 200);
        var q = db.AuditEntries.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(query.SubjectType))
        {
            q = q.Where(e => e.SubjectType == query.SubjectType);
        }

        if (!string.IsNullOrWhiteSpace(query.SubjectId))
        {
            q = q.Where(e => e.SubjectId == query.SubjectId);
        }

        if (query.BeforeSequence is { } before)
        {
            q = q.Where(e => e.Sequence < before);
        }

        var entries = await q.OrderByDescending(e => e.Sequence).Take(limit).ToListAsync(ct);
        return Result.Success(await AuditEntryDtoMapper.MapAsync(entries, db, ct));
    }
}
