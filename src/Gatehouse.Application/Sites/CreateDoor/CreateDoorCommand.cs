using Gatehouse.Application.Sites.Dtos;

namespace Gatehouse.Application.Sites.CreateDoor;

public sealed record CreateDoorCommand(Guid AreaId, SaveDoorRequest Request);
