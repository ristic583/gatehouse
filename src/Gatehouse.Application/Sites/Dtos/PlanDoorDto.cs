using Gatehouse.Application.Access.Dtos;

namespace Gatehouse.Application.Sites.Dtos;

public sealed record PlanDoorDto(Guid Id, string Name, PointDto Position, string? PacsDoorRef, IReadOnlyList<PlanReaderDto> Readers);
