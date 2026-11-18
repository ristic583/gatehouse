using Gatehouse.Application.Access.Dtos;

namespace Gatehouse.Application.LiveBuilding.GetLiveFloor;

public sealed record LiveAreaDto(Guid AreaId, string Name, string Code, IReadOnlyList<PointDto> Polygon);
