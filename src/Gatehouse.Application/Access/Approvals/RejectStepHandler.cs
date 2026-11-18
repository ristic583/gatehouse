using Gatehouse.Application.Access.Dtos;
using Gatehouse.Domain.Common;

namespace Gatehouse.Application.Access.Approvals;

public sealed class RejectStepHandler(StepDecisionService decisions)
{
    public Task<Result<AccessRequestDto>> HandleAsync(DecideStepCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        return decisions.DecideAsync(command, (e, step, me, now) => e.Reject(step, me, command.Comment ?? string.Empty, now), ct);
    }
}
