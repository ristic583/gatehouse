using Gatehouse.Application.Sites.Dtos;

namespace Gatehouse.Application.Sites.CreateReader;

public sealed record CreateReaderCommand(Guid DoorId, SaveReaderRequest Request);
