using Gatehouse.Application.Sites.Dtos;

namespace Gatehouse.Application.Sites.UpdateArea;

public sealed record UpdateAreaCommand(Guid AreaId, SaveAreaRequest Request);
