using Gatehouse.Domain.Common;

namespace Gatehouse.Application.Credentials.Photos;

public static class PhotoErrors
{
    /// <summary>Also returned when the caller may not view the photo, so existence does not leak.</summary>
    public static Error NotFound { get; } = Error.NotFound("photo.not_found", "Photo not found.");
}
