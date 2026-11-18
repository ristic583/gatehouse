using System.Text;
using Gatehouse.Domain.Credentials;
using Gatehouse.Infrastructure.Storage;
using SkiaSharp;

namespace Gatehouse.UnitTests.Credentials;

public sealed class PhotoProcessorTests
{
    private static readonly PhotoProcessor Processor = new();

    private static byte[] Image(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(new SKColor(30, 90, 160));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    [Fact]
    public void Exif_is_stripped()
    {
        var jpeg = Image(64, 64, SKEncodedImageFormat.Jpeg);
        var marker = Encoding.ASCII.GetBytes("Exif\0\0GPS-SECRET-55.6761N");
        var length = marker.Length + 2;
        var app1 = new byte[] { 0xFF, 0xE1, (byte)(length >> 8), (byte)(length & 0xFF) }.Concat(marker);
        var withExif = jpeg.Take(2).Concat(app1).Concat(jpeg.Skip(2)).Concat(Encoding.ASCII.GetBytes("TRAILING-DATA")).ToArray();

        var result = Processor.Process(withExif);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        var output = Encoding.ASCII.GetString(result.Value.Content);
        Assert.DoesNotContain("GPS-SECRET", output, StringComparison.Ordinal);
        Assert.DoesNotContain("TRAILING-DATA", output, StringComparison.Ordinal);
        Assert.Equal(Photo.Jpeg, result.Value.ContentType);
    }

    [Fact]
    public void Gif_is_rejected()
    {
        var gif = Encoding.ASCII.GetBytes("GIF89a").Concat(new byte[64]).ToArray();
        var fakePng = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };

        Assert.Equal("photo.unsupported_type", Processor.Process(gif).Error.Code);
        Assert.Equal("photo.unsupported_type", Processor.Process(fakePng).Error.Code);
        Assert.Equal("photo.unsupported_type", Processor.Process([]).Error.Code);
    }

    [Fact]
    public void Oversize_is_rejected_before_decode()
    {
        var huge = new byte[Photo.MaxSizeBytes + 1];
        huge[0] = 0xFF;
        huge[1] = 0xD8;
        huge[2] = 0xFF;

        Assert.Equal("photo.too_large", Processor.Process(huge).Error.Code);
    }

    [Fact]
    public void Large_image_is_downscaled()
    {
        var result = Processor.Process(Image(2000, 1000, SKEncodedImageFormat.Png));

        Assert.True(result.IsSuccess);
        Assert.Equal(PhotoProcessor.MaxEdge, result.Value.Width);
        Assert.Equal(512, result.Value.Height);
        Assert.Equal(Photo.Png, result.Value.ContentType);
    }
}
