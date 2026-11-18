namespace Gatehouse.Application.Sites.UploadFloorBackground;

public sealed record UploadFloorBackgroundCommand(Guid FloorId, byte[] Upload);
