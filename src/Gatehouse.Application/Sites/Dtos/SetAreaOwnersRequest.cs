namespace Gatehouse.Application.Sites.Dtos;

public sealed record SetAreaOwnersRequest(IReadOnlyList<Guid> PersonIds);
