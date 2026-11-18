using Gatehouse.Application.Authorization;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Identity;
using Gatehouse.Application.Tasks.Dtos;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Tasks.ListStaffTasks;

public sealed class ListStaffTasksHandler(ITenantDb db, ICurrentUser me, StaffTaskDtoBuilder builder)
{
    public async Task<Result<IReadOnlyList<StaffTaskDto>>> HandleAsync(ListStaffTasksQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var visible = db.StaffTasks.AsNoTracking().VisibleTo(me);
        var resolved = string.Equals(query.Status, "resolved", StringComparison.OrdinalIgnoreCase);
        var rows = resolved
            ? await visible.Where(t => t.Status == StaffTaskStatus.Resolved).OrderByDescending(t => t.ResolvedAt).Take(50).ToListAsync(ct)
            : await visible.Where(t => t.Status != StaffTaskStatus.Resolved).OrderBy(t => t.CreatedAt).ToListAsync(ct);
        return Result.Success(await builder.BuildAsync(rows, ct));
    }
}
