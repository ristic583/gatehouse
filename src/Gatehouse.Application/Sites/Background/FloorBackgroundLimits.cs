namespace Gatehouse.Application.Sites.Background;

/// <summary>
/// Customer floor drawings (<c>domain-model.md</c> §2.10): JPEG/PNG sniffed, ≤ 10 MiB before decode, sources ≤ 8192 px,
/// re-encoded ≤ 4096 px without metadata.
/// </summary>
public static class FloorBackgroundLimits
{
    public const long MaxBytes = 10 * 1024 * 1024;
    public const int MaxSourceEdge = 8192;
    public const int MaxEdge = 4096;
    public const string ErrorPrefix = "floor.background_";
}
