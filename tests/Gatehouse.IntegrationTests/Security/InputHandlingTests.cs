using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Gatehouse.Application.People.Dtos;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Security;

/// <summary>Security regression (M11, area 4): CSV formula cells and uploads with a wrong type or hidden payload.</summary>
public sealed class InputHandlingTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatehouseApiFactory _api = new(postgres);

    public ValueTask InitializeAsync() => _api.InitializeAsync();

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    [Fact]
    public async Task Csv_import_refuses_every_formula_prefix()
    {
        var ct = TestContext.Current.CancellationToken;
        using var mia = _api.CreateClientFor(DemoUser.Mia);
        string[] cells = ["=1+1", "+1+1", "-1+1", "@SUM(A1)", " =1+1", "\t=1+1", "\"=HYPERLINK(\"\"http://x\"\")\""];
        var csv = new StringBuilder("email,given_name,family_name\n");
        for (var i = 0; i < cells.Length; i++)
        {
            csv.Append(System.Globalization.CultureInfo.InvariantCulture, $"formula{i}@bricks.test,Name,{cells[i]}\n");
        }

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(csv.ToString()));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(file, "file", "people.csv");
        using var response = await mia.PostAsync(new Uri("/api/v1/admin/people/import?dryRun=false", UriKind.Relative), form, ct);

        var result = (await response.Content.ReadFromJsonAsync<PeopleImportResultDto>(ApiJson.Options, ct))!;
        Assert.Equal(0, result.Created);
        Assert.Equal(cells.Length, result.Errors.Count);
        Assert.All(result.Errors, e => Assert.Equal(("people_import.invalid_file", "family_name"), (e.Code, e.Field)));
        await using var scope = await _api.CreateTenantScopeAsync("bricks");
        Assert.False(await scope.ServiceProvider.GetRequiredService<TenantDbContext>().Persons.AnyAsync(p => p.Email.StartsWith("formula"), ct));
    }

    [Fact]
    public async Task Photo_that_is_not_an_image_is_rejected_whatever_its_content_type()
    {
        var ct = TestContext.Current.CancellationToken;
        using var lena = _api.CreateClientFor(DemoUser.Lena);
        var svg = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>");
        var html = Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>");
        var truncatedPng = SecurityUploads.Png(16, 16)[..20];

        foreach (var (bytes, type) in new[] { (svg, "image/png"), (svg, "image/svg+xml"), (html, "image/jpeg"), (truncatedPng, "image/png") })
        {
            using var response = await SecurityUploads.UploadAsync(lena, "/api/v1/me/photo", bytes, ct, type);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("photo.unsupported_type", await response.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Floor_drawing_that_is_not_an_image_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        using var mia = _api.CreateClientFor(DemoUser.Mia);
        var floorId = await Access.AccessMapEndpointTests.FloorIdAsync(_api, "bricks", "BER-OFF", 0);
        var svg = Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(1)\"/>");

        using var response = await SecurityUploads.UploadAsync(mia, $"/api/v1/admin/floors/{floorId}/background", svg, ct, "image/svg+xml");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Image_with_appended_markup_is_re_encoded_before_it_is_served()
    {
        var ct = TestContext.Current.CancellationToken;
        using var lena = _api.CreateClientFor(DemoUser.Lena);
        var marker = "<script>alert('polyglot')</script>"u8.ToArray();
        var polyglot = SecurityUploads.Png(8, 8).Concat(marker).ToArray();
        var lenaId = await Access.AccessRequestEndpointTests.PersonIdAsync(_api, "bricks", DemoUser.Lena.Email);

        using var upload = await SecurityUploads.UploadAsync(lena, "/api/v1/me/photo", polyglot, ct, "text/html");
        using var served = await lena.GetAsync(new Uri($"/api/v1/people/{lenaId}/photo", UriKind.Relative), ct);
        var bytes = await served.Content.ReadAsByteArrayAsync(ct);

        Assert.Equal(HttpStatusCode.NoContent, upload.StatusCode);
        Assert.Equal("image/png", served.Content.Headers.ContentType?.MediaType);
        Assert.Contains("nosniff", served.Headers.GetValues("X-Content-Type-Options"));
        Assert.Equal(-1, bytes.AsSpan().IndexOf(marker));
    }
}
