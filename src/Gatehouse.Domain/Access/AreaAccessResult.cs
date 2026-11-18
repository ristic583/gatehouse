namespace Gatehouse.Domain.Access;

public sealed record AreaAccessResult(AreaAccessState State, AreaAccessReason Reason, bool CardInactive, DateTimeOffset? Since);
