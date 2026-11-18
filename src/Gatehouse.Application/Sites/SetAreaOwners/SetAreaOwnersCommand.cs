using Gatehouse.Application.Sites.Dtos;

namespace Gatehouse.Application.Sites.SetAreaOwners;

public sealed record SetAreaOwnersCommand(Guid AreaId, SetAreaOwnersRequest Request);
