using Gatehouse.Application.Tasks.Dtos;
using Gatehouse.Domain.Common;

namespace Gatehouse.Application.Tasks.StaffTaskActions;

public sealed class ClaimStaffTaskHandler(StaffTaskActionService service)
{
    public Task<Result<StaffTaskDto>> HandleAsync(Guid taskId, CancellationToken ct) =>
        service.RunAsync(taskId, (t, me, now) => t.Claim(me, now), "task.claimed", _ => new { }, ct);
}
