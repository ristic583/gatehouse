using Gatehouse.Domain.LiveBuilding;

namespace Gatehouse.Application.LiveBuilding.RequestDoorCommand;

/// <summary>Body of <c>POST /api/v1/live/door-commands</c>.</summary>
public sealed record RequestDoorCommandCommand(
    DoorCommandKind Kind, DoorCommandScope Scope, Guid ScopeId, string? Reason, int? DurationSeconds);
