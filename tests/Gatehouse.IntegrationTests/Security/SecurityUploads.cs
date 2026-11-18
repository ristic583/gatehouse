using System.Net.Http.Headers;
using SkiaSharp;

namespace Gatehouse.IntegrationTests.Security;

/// <summary>Multipart upload and tiny PNG helpers shared by the security regression tests.</summary>
public static class SecurityUploads
{
    public static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string url, byte[] bytes, CancellationToken ct, string contentType = "image/png")
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "file", "upload.png");
        return await client.PutAsync(new Uri(url, UriKind.Relative), form, ct);
    }

    public static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.SteelBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
