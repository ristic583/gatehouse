namespace Gatehouse.Application.Access.Dtos;

public sealed record CreateAccessRequestDto(
    Guid? PersonId, Guid AreaId, DateTimeOffset RequestedStart, DateTimeOffset RequestedEnd, string Justification);
