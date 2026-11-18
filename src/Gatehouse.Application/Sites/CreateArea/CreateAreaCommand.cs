using Gatehouse.Application.Sites.Dtos;

namespace Gatehouse.Application.Sites.CreateArea;

public sealed record CreateAreaCommand(Guid FloorId, SaveAreaRequest Request);
