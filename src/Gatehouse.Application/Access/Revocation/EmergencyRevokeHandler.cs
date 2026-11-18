using Gatehouse.Application.Access.Dtos;
using Gatehouse.Domain.Common;

namespace Gatehouse.Application.Access.Revocation;

public sealed class EmergencyRevokeHandler(RevocationService service)
{
    public Task<Result<AccessRequestDto>> HandleAsync(RevokeEntitlementCommand command, CancellationToken ct) =>
        service.RevokeAsync(command, true, ct);
}
