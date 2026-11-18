namespace Gatehouse.Application.Credentials.Photos;

/// <summary>A re-encoded photo (no EXIF or trailing data) ready to store.</summary>
public sealed record ProcessedPhoto(byte[] Content, string ContentType, int Width, int Height);
