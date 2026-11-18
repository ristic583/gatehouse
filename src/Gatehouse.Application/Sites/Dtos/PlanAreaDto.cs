using Gatehouse.Application.Access.Dtos;
using Gatehouse.Application.People.Dtos;

namespace Gatehouse.Application.Sites.Dtos;

public sealed record PlanAreaDto(
    Guid Id,
    string Name,
    string Code,
    IReadOnlyList<PointDto> Polygon,
    Guid SecurityLevelId,
    IReadOnlyList<PersonRefDto> Owners,
    IReadOnlyList<PlanDoorDto> Doors,
    bool AccessControlled = true);
