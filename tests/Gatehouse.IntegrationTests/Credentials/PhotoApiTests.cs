using System.Net;
using System.Net.Http.Headers;
using Gatehouse.Application.Credentials.Photos;
using Gatehouse.Domain.People;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.Infrastructure.Storage;
using Gatehouse.IntegrationTests.Infrastructure;
using Gatehouse.IntegrationTests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace Gatehouse.IntegrationTests.Credentials;

/// <summary>Photo upload and viewing (M9-04) for fresh employees managed by mikkel; people (and their photos) are removed afterwards.</summary>
public sealed class PhotoApiTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Tenant = "nordlys";
    private static readonly Uri MyPhoto = new("/api/v1/me/photo", UriKind.Relative);

    private readonly GatehouseApiFactory _api = new(postgres);
    private readonly List<PersonId> _people = [];

    public ValueTask InitializeAsync() => _api.InitializeAsync();

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    [Fact]
    public async Task Upload_and_view_own_photo()
    {
        var ct = TestContext.Current.CancellationToken;
        var (personId, client) = await EmployeeAsync(ct);
        using (client)
        {
            using var upload = await UploadAsync(client, Jpeg(), ct);
            using var view = await client.GetAsync(PhotoOf(personId), ct);

            Assert.Equal(HttpStatusCode.NoContent, upload.StatusCode);
            Assert.Equal(HttpStatusCode.OK, view.StatusCode);
            Assert.Equal("image/jpeg", view.Content.Headers.ContentType?.MediaType);
            Assert.Contains("no-store", view.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
        }

        Assert.False(await AuditedAsync("photo.viewed", personId, ct));
        Assert.True(await AuditedAsync("photo.uploaded", personId, ct));
    }

    [Fact]
    public async Task Manager_view_is_audited()
    {
        var ct = TestContext.Current.CancellationToken;
        var (personId, client) = await EmployeeAsync(ct);
        using (client)
        using (var upload = await UploadAsync(client, Jpeg(), ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, upload.StatusCode);
        }

        using var mikkel = _api.CreateClientFor(DemoUser.Mikkel);
        using var view = await mikkel.GetAsync(PhotoOf(personId), ct);

        Assert.Equal(HttpStatusCode.OK, view.StatusCode);
        Assert.True(await AuditedAsync("photo.viewed", personId, ct));
    }

    [Fact]
    public async Task Stranger_gets_404()
    {
        var ct = TestContext.Current.CancellationToken;
        var (personId, client) = await EmployeeAsync(ct);
        using (client)
        using (var upload = await UploadAsync(client, Jpeg(), ct))
        {
            Assert.Equal(HttpStatusCode.NoContent, upload.StatusCode);
        }

        using var anna = _api.CreateClientFor(DemoUser.Anna);
        using var view = await anna.GetAsync(PhotoOf(personId), ct);

        Assert.Equal(HttpStatusCode.NotFound, view.StatusCode);
        Assert.Contains("photo.not_found", await view.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Upload_replaces_old_object()
    {
        var ct = TestContext.Current.CancellationToken;
        var (personId, client) = await EmployeeAsync(ct);
        using (client)
        {
            using var first = await UploadAsync(client, Jpeg(), ct);
            using var second = await UploadAsync(client, Png(), ct);
            using var gif = await UploadAsync(client, "GIF89a-not-an-image"u8.ToArray(), ct);

            Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, gif.StatusCode);
            Assert.Contains("photo.unsupported_type", await gif.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
        }

        Assert.Single(KeysOf(personId));
        Assert.EndsWith(".png", KeysOf(personId).Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Delete_removes_object()
    {
        var ct = TestContext.Current.CancellationToken;
        var (personId, client) = await EmployeeAsync(ct);
        using (client)
        {
            using var upload = await UploadAsync(client, Jpeg(), ct);
            using var delete = await client.DeleteAsync(MyPhoto, ct);
            using var view = await client.GetAsync(PhotoOf(personId), ct);

            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, view.StatusCode);
        }

        Assert.Empty(KeysOf(personId));
        Assert.True(await AuditedAsync("photo.deleted", personId, ct));
    }

    private List<string> KeysOf(PersonId personId) =>
        ((InMemoryObjectStore)_api.Services.GetRequiredService<IObjectStore>()).Keys.Where(k => k.Contains(personId.Value.ToString(), StringComparison.Ordinal)).ToList();

    private static Uri PhotoOf(PersonId id) => new($"/api/v1/people/{id.Value}/photo", UriKind.Relative);

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, byte[] bytes, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", "photo.bin");
        return await client.PutAsync(MyPhoto, form, ct);
    }

    private static byte[] Jpeg() => Encode(SKEncodedImageFormat.Jpeg);

    private static byte[] Png() => Encode(SKEncodedImageFormat.Png);

    private static byte[] Encode(SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(32, 32);
        bitmap.Erase(SKColors.SteelBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    private async Task<bool> AuditedAsync(string action, PersonId personId, CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var subject = personId.Value.ToString();
        return await scope.ServiceProvider.GetRequiredService<TenantDbContext>().AuditEntries.AsNoTracking().AnyAsync(a => a.Action == action && a.SubjectId == subject, ct);
    }

    private async Task<(PersonId PersonId, HttpClient Client)> EmployeeAsync(CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var mikkel = await db.Persons.AsNoTracking().SingleAsync(p => p.Email == DemoUser.Mikkel.Email, ct);
        var email = $"ph-{Guid.NewGuid():N}@nordlys.test";
        var person = MappingData.Employee(email);
        person.SetManager(mikkel.Id);
        person.ClearDomainEvents();
        db.Persons.Add(person);
        _people.Add(person.Id);
        await db.SaveChangesAsync(ct);
        return (person.Id, _api.CreateClientFor($"sub-{Guid.NewGuid():N}", email, Tenant));
    }
}
