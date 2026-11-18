using Gatehouse.Application.Sites.Dtos;

namespace Gatehouse.Application.Sites.UpdateReader;

public sealed record UpdateReaderCommand(Guid ReaderId, SaveReaderRequest Request);
