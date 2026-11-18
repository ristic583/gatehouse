namespace Gatehouse.Application.Sites.Background;

public sealed record FloorBackgroundContent(byte[] Content, string ContentType, DateTimeOffset UpdatedAt);
