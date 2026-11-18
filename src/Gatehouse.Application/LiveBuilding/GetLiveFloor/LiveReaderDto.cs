using Gatehouse.Application.Access.Dtos;

namespace Gatehouse.Application.LiveBuilding.GetLiveFloor;

public sealed record LiveReaderDto(Guid ReaderId, string Name, PointDto Position);
