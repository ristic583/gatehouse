using Gatehouse.Application.Access.Dtos;
using Gatehouse.Domain.Sites;

namespace Gatehouse.Application.Sites.Dtos;

public sealed record PlanReaderDto(Guid Id, string Name, ReaderDirection Direction, PointDto Position, string? PacsReaderRef);
