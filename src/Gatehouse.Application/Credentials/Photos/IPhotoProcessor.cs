using Gatehouse.Domain.Common;

namespace Gatehouse.Application.Credentials.Photos;

/// <summary>Sniffs, size-checks, decodes, downscales and re-encodes an uploaded image (JPEG/PNG only).</summary>
public interface IPhotoProcessor
{
    /// <summary>Person photo rules (<c>photo.too_large</c>, <c>photo.unsupported_type</c>).</summary>
    public Result<ProcessedPhoto> Process(ReadOnlySpan<byte> upload);

    /// <summary>
    /// Same pipeline with other limits (M10 floor backgrounds): at most <paramref name="maxBytes"/> before decoding,
    /// sources up to <paramref name="maxSourceEdge"/> px, output ≤ <paramref name="maxEdge"/> px; errors are
    /// <c>{errorPrefix}too_large</c> and <c>{errorPrefix}unsupported_type</c> (the prefix includes its separator: <c>photo.</c>,
    /// <c>floor.background_</c>).
    /// </summary>
    public Result<ProcessedPhoto> Process(ReadOnlySpan<byte> upload, long maxBytes, int maxSourceEdge, int maxEdge, string errorPrefix);
}
