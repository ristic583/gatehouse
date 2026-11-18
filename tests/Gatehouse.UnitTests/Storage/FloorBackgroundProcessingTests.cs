using System.Text;
using Gatehouse.Application.Sites.Background;
using Gatehouse.Infrastructure.Storage;
using SkiaSharp;

namespace Gatehouse.UnitTests.Storage;

/// <summary><c>domain-model.md</c> §2.10 floor background processing through the photo pipeline.</summary>
public sealed class FloorBackgroundProcessingTests
{
    private static readonly PhotoProcessor Processor = new();

    [Fact]
    public void Svg_is_rejected()
    {
        var svg = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"10\" height=\"10\"><script>alert(1)</script></svg>");

        var result = Process(svg);

        Assert.Equal("floor.background_unsupported_type", result.Error.Code);
    }

    [Fact]
    public void Oversize_is_rejected_before_decode()
    {
        // Not an image at all: the size check must fire before sniffing or decoding.
        var huge = new byte[FloorBackgroundLimits.MaxBytes + 1];

        var result = Process(huge);

        Assert.Equal("floor.background_too_large", result.Error.Code);
    }

    [Fact]
    public void Large_drawing_is_downscaled_to_4096()
    {
        var result = Process(Image(6000, 3000, SKEncodedImageFormat.Png));

        Assert.True(result.IsSuccess);
        Assert.Equal((4096, 2048), (result.Value.Width, result.Value.Height));
        Assert.Equal("image/png", result.Value.ContentType);
        using var decoded = SKBitmap.Decode(result.Value.Content);
        Assert.Equal(4096, decoded.Width);
    }

    [Fact]
    public void Metadata_is_stripped()
    {
        var jpeg = Image(800, 600, SKEncodedImageFormat.Jpeg);
        var marker = Encoding.ASCII.GetBytes("Exif\0\0ARCHITECT-SECRET-PLAN-42");
        var length = marker.Length + 2;
        byte[] app1 = [0xFF, 0xE1, (byte)(length >> 8), (byte)(length & 0xFF), .. marker];
        var withExif = jpeg.Take(2).Concat(app1).Concat(jpeg.Skip(2)).Concat(Encoding.ASCII.GetBytes("TRAILING-DATA")).ToArray();

        var result = Process(withExif);

        Assert.True(result.IsSuccess);
        var text = Encoding.ASCII.GetString(result.Value.Content);
        Assert.DoesNotContain("ARCHITECT-SECRET", text, StringComparison.Ordinal);
        Assert.DoesNotContain("TRAILING-DATA", text, StringComparison.Ordinal);
        Assert.Equal((800, 600), (result.Value.Width, result.Value.Height));
    }

    private static Gatehouse.Domain.Common.Result<Gatehouse.Application.Credentials.Photos.ProcessedPhoto> Process(byte[] bytes) =>
        Processor.Process(bytes, FloorBackgroundLimits.MaxBytes, FloorBackgroundLimits.MaxSourceEdge, FloorBackgroundLimits.MaxEdge, FloorBackgroundLimits.ErrorPrefix);

    private static byte[] Image(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(new SKColor(200, 200, 190));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }
}
