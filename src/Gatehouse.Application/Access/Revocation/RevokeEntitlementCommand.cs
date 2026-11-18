namespace Gatehouse.Application.Access.Revocation;

public sealed record RevokeEntitlementCommand(Guid EntitlementId, string? Reason);
