using Gatehouse.Domain.LiveBuilding;

namespace Gatehouse.Application.LiveBuilding.Dtos;

/// <summary>Cached door state (Redis key <c>tenant:{tenantId}:door:{doorId}:state</c>).</summary>
public sealed record DoorStateDto(Guid DoorId, DoorState State, DateTimeOffset Since, DateTimeOffset LastEventAt);
