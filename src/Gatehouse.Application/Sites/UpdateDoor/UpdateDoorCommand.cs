using Gatehouse.Application.Sites.Dtos;

namespace Gatehouse.Application.Sites.UpdateDoor;

public sealed record UpdateDoorCommand(Guid DoorId, SaveDoorRequest Request);
