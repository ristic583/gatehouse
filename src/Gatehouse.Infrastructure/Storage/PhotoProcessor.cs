using Gatehouse.Application.Credentials.Photos;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Credentials;
using SkiaSharp;

namespace Gatehouse.Infrastructure.Storage;

/// <summary>
/// Magic-byte sniffing (JPEG <c>FF D8 FF</c>, PNG <c>89 50 4E 47</c>), size check before decoding, decode with a pixel cap, downscale to
/// ≤ <see cref="MaxEdge"/> px and re-encode. Re-encoding writes fresh pixels only, so EXIF, ICC comments and trailing data are dropped.
/// </summary>
public sealed class PhotoProcessor : IPhotoProcessor
{
    public const int MaxEdge = 1024;
    public const int MaxSourceEdge = 4096;
    private const int JpegQuality = 85;

    public Result<ProcessedPhoto> Process(ReadOnlySpan<byte> upload) =>
        Process(upload, Photo.MaxSizeBytes, MaxSourceEdge, MaxEdge, "photo.");

    public Result<ProcessedPhoto> Process(ReadOnlySpan<byte> upload, long maxBytes, int maxSourceEdge, int maxEdge, string errorPrefix)
    {
        if (upload.Length > maxBytes)
        {
            return Error.Validation($"{errorPrefix}too_large", $"The image may be at most {maxBytes / (1024 * 1024)} MiB.");
        }

        var contentType = Sniff(upload);
        if (contentType is null)
        {
            return Unsupported(errorPrefix);
        }

        using var data = SKData.CreateCopy(upload);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.Info.Width is <= 0 || codec.Info.Width > maxSourceEdge || codec.Info.Height is <= 0
            || codec.Info.Height > maxSourceEdge)
        {
            return Unsupported(errorPrefix);
        }

        // Let the codec decode at a reduced size where it can (JPEG 1/2, 1/4, 1/8) so large drawings never decode at full resolution.
        var target = Math.Min(1f, (float)maxEdge / Math.Max(codec.Info.Width, codec.Info.Height));
        var scaled = codec.GetScaledDimensions(target);
        var info = new SKImageInfo(scaled.Width, scaled.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var source = new SKBitmap(info);
        var decoded = codec.GetPixels(info, source.GetPixels());
        if (decoded is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            return Unsupported(errorPrefix);
        }

        var scale = Math.Min(1.0, (double)maxEdge / Math.Max(source.Width, source.Height));
        var width = Math.Max(1, (int)Math.Round(source.Width * scale));
        var height = Math.Max(1, (int)Math.Round(source.Height * scale));
        using var resized = scale < 1.0
            ? source.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
            : null;
        var bitmap = resized ?? source;
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = contentType == Photo.Png
            ? image.Encode(SKEncodedImageFormat.Png, 100)
            : image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
        if (encoded is null)
        {
            return Unsupported(errorPrefix);
        }

        return new ProcessedPhoto(encoded.ToArray(), contentType, bitmap.Width, bitmap.Height);
    }

    private static string? Sniff(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return Photo.Jpeg;
        }

        return bytes.Length >= 8 && bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })
            ? Photo.Png
            : null;
    }

    private static Error Unsupported(string errorPrefix) =>
        Error.Validation($"{errorPrefix}unsupported_type", "Only JPEG and PNG images are accepted.");
}
