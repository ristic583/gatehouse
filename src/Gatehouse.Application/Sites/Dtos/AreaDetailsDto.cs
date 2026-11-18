using Gatehouse.Application.People.Dtos;

namespace Gatehouse.Application.Sites.Dtos;

public sealed record AreaDetailsDto(
    Guid AreaId,
    string Name,
    string Code,
    Guid FloorId,
    Guid SiteId,
    SecurityLevelDto SecurityLevel,
    IReadOnlyList<PersonRefDto> Owners);
