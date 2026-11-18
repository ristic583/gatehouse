using Gatehouse.Domain.Common;

namespace Gatehouse.Application.Sites.Admin;

public static class SiteErrors
{
    public static Error SiteNotFound { get; } = Error.NotFound("site.not_found", "The site does not exist.");

    public static Error FloorNotFound { get; } = Error.NotFound("floor.not_found", "The floor does not exist.");

    public static Error AreaNotFound { get; } = Error.NotFound("area.not_found", "The area does not exist.");

    public static Error DoorNotFound { get; } = Error.NotFound("door.not_found", "The door does not exist.");

    public static Error ReaderNotFound { get; } = Error.NotFound("reader.not_found", "The reader does not exist.");

    public static Error BackgroundNotFound { get; } = Error.NotFound("floor.background_not_found", "The floor has no background image.");
}
