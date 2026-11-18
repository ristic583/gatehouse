using Gatehouse.Application.Sites.Dtos;

namespace Gatehouse.Application.Sites.UpdateFloor;

public sealed record UpdateFloorCommand(Guid FloorId, SaveFloorRequest Request);
