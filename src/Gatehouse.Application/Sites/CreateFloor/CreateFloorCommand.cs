using Gatehouse.Application.Sites.Dtos;

namespace Gatehouse.Application.Sites.CreateFloor;

public sealed record CreateFloorCommand(Guid SiteId, SaveFloorRequest Request);
