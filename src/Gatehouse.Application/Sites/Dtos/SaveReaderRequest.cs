using Gatehouse.Application.Access.Dtos;
using Gatehouse.Domain.Sites;

namespace Gatehouse.Application.Sites.Dtos;

public sealed record SaveReaderRequest(string Name, ReaderDirection Direction, PointDto Position, string? PacsReaderRef);
