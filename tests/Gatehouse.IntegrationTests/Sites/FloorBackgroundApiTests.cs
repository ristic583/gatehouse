using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Gatehouse.Application.Access.Dtos;
using Gatehouse.Application.Credentials.Photos;
using Gatehouse.Application.Sites.Dtos;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace Gatehouse.IntegrationTests.Sites;

/// <summary>authorization.md §11 floor background rows.</summary>
public sealed class FloorBackgroundApiTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatehouseApiFactory _api = new(postgres);

    public ValueTask InitializeAsync() => _api.InitializeAsync();

    public async ValueTask DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Upload_replace_and_delete_background()
    {
        var ct = TestContext.Current.CancellationToken;
        var floorId = await FloorIdAsync(ct);
        using var freja = _api.CreateClientFor(DemoUser.Freja);
        var store = _api.Services.GetRequiredService<IObjectStore>();

        using var first = await UploadAsync(freja, floorId, Image(1200, 800, SKEncodedImageFormat.Png), ct);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        var firstKey = await BackgroundKeyAsync(floorId, ct);
        Assert.StartsWith($"tenant/nordlys/floors/{floorId}/", firstKey, StringComparison.Ordinal);
        Assert.EndsWith(".png", firstKey, StringComparison.Ordinal);

        using var second = await UploadAsync(freja, floorId, Image(900, 600, SKEncodedImageFormat.Jpeg), ct);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        var secondKey = await BackgroundKeyAsync(floorId, ct);
        Assert.NotEqual(firstKey, secondKey);
        Assert.Null(await store.GetAsync(firstKey!, ct));

        using var unsupported = await UploadAsync(freja, floorId, "%PDF-1.7 not an image"u8.ToArray(), ct);
        Assert.Equal(HttpStatusCode.BadRequest, unsupported.StatusCode);
        Assert.Contains("floor.background_unsupported_type", await unsupported.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);

        using var delete = await freja.DeleteAsync(BackgroundAdmin(floorId), ct);
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Null(await BackgroundKeyAsync(floorId, ct));
        Assert.Null(await store.GetAsync(secondKey!, ct));
        using var anna = _api.CreateClientFor(DemoUser.Anna);
        using var gone = await anna.GetAsync(Background(floorId), ct);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Contains("floor.background_not_found", await gone.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Employee_can_view_background()
    {
        var ct = TestContext.Current.CancellationToken;
        var floorId = await FloorIdAsync(ct);
        using var freja = _api.CreateClientFor(DemoUser.Freja);
        using var upload = await UploadAsync(freja, floorId, Image(640, 480, SKEncodedImageFormat.Jpeg), ct);
        Assert.Equal(HttpStatusCode.NoContent, upload.StatusCode);

        using var anna = _api.CreateClientFor(DemoUser.Anna);
        using var response = await anna.GetAsync(Background(floorId), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/jpeg", response.Content.Headers.ContentType!.MediaType);
        Assert.True(response.Headers.CacheControl!.Private);
        Assert.Equal(TimeSpan.FromSeconds(300), response.Headers.CacheControl.MaxAge);
        Assert.Contains("nosniff", response.Headers.GetValues("X-Content-Type-Options"));
        Assert.NotNull(response.Headers.ETag);

        using var conditional = new HttpRequestMessage(HttpMethod.Get, Background(floorId));
        conditional.Headers.IfNoneMatch.Add(response.Headers.ETag!);
        using var notModified = await anna.SendAsync(conditional, ct);
        Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
    }

    [Fact]
    public async Task Non_admin_upload_is_403()
    {
        var ct = TestContext.Current.CancellationToken;
        var floorId = await FloorIdAsync(ct);
        using var anna = _api.CreateClientFor(DemoUser.Anna);

        using var response = await UploadAsync(anna, floorId, Image(100, 100, SKEncodedImageFormat.Png), ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Access_map_reports_has_background()
    {
        var ct = TestContext.Current.CancellationToken;
        var floorId = await FloorIdAsync(ct);
        using var freja = _api.CreateClientFor(DemoUser.Freja);
        using var upload = await UploadAsync(freja, floorId, Image(500, 400, SKEncodedImageFormat.Png), ct);
        Assert.Equal(HttpStatusCode.NoContent, upload.StatusCode);

        using var anna = _api.CreateClientFor(DemoUser.Anna);
        var map = (await anna.GetFromJsonAsync<FloorAccessMapDto>(new Uri($"/api/v1/floors/{floorId}/access-map", UriKind.Relative), ApiJson.Options, ct))!;
        var plan = (await freja.GetFromJsonAsync<FloorPlanDto>(new Uri($"/api/v1/admin/floors/{floorId}/plan", UriKind.Relative), ApiJson.Options, ct))!;

        Assert.True(map.HasBackground);
        Assert.NotNull(map.BackgroundUpdatedAt);
        Assert.True(plan.HasBackground);
    }

    private static Uri Background(Guid floorId) => new($"/api/v1/floors/{floorId}/background", UriKind.Relative);

    private static Uri BackgroundAdmin(Guid floorId) => new($"/api/v1/admin/floors/{floorId}/background", UriKind.Relative);

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, Guid floorId, byte[] bytes, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", "drawing.bin");
        return await client.PutAsync(BackgroundAdmin(floorId), form, ct);
    }

    private static byte[] Image(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(new SKColor(220, 220, 210));
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    private async Task<Guid> FloorIdAsync(CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync("nordlys");
        return (await scope.ServiceProvider.GetRequiredService<TenantDbContext>().Areas.AsNoTracking().Select(a => a.FloorId).FirstAsync(ct)).Value;
    }

    private async Task<string?> BackgroundKeyAsync(Guid floorId, CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync("nordlys");
        var id = new Gatehouse.Domain.Sites.FloorId(floorId);
        return await scope.ServiceProvider.GetRequiredService<TenantDbContext>().Floors.AsNoTracking().Where(f => f.Id == id).Select(f => f.BackgroundObjectKey).SingleAsync(ct);
    }
}
