using System.Net;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.People;

/// <summary>GDPR export (M9-06) of anna by the tenant admin.</summary>
public sealed class PersonExportTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatehouseApiFactory _api = new(postgres);

    public ValueTask InitializeAsync() => _api.InitializeAsync();

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    [Fact]
    public async Task Admin_exports_person_data()
    {
        var ct = TestContext.Current.CancellationToken;
        var (annaId, _) = await AnnaAsync(ct);
        using var freja = _api.CreateClientFor(DemoUser.Freja);

        using var response = await freja.GetAsync(new Uri($"/api/v1/people/{annaId}/export", UriKind.Relative), ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("attachment", response.Content.Headers.ContentDisposition?.ToString() ?? response.Headers.GetValues("Content-Disposition").FirstOrDefault() ?? string.Empty, StringComparison.Ordinal);
        var body = await response.Content.ReadAsStringAsync(ct);
        Assert.Contains(DemoUser.Anna.Email, body, StringComparison.Ordinal);
        Assert.Contains("\"entitlements\"", body, StringComparison.Ordinal);
        Assert.Contains("\"auditEntries\"", body, StringComparison.Ordinal);
        await using var scope = await _api.CreateTenantScopeAsync("nordlys");
        var subject = annaId.ToString();
        Assert.True(await scope.ServiceProvider.GetRequiredService<TenantDbContext>().AuditEntries.AsNoTracking().AnyAsync(a => a.Action == "person.exported" && a.SubjectId == subject, ct));
    }

    [Fact]
    public async Task Non_admin_cannot_export()
    {
        var ct = TestContext.Current.CancellationToken;
        var (annaId, _) = await AnnaAsync(ct);
        using var mikkel = _api.CreateClientFor(DemoUser.Mikkel);

        using var response = await mikkel.GetAsync(new Uri($"/api/v1/people/{annaId}/export", UriKind.Relative), ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Export_masks_card_numbers()
    {
        var ct = TestContext.Current.CancellationToken;
        var (annaId, cardNumber) = await AnnaAsync(ct);
        using var freja = _api.CreateClientFor(DemoUser.Freja);

        var body = await freja.GetStringAsync(new Uri($"/api/v1/people/{annaId}/export", UriKind.Relative), ct);

        Assert.NotNull(cardNumber);
        Assert.DoesNotContain(cardNumber, body, StringComparison.Ordinal);
        Assert.Contains(cardNumber[^4..], body, StringComparison.Ordinal);
    }

    private async Task<(Guid Id, string? CardNumber)> AnnaAsync(CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var anna = await db.Persons.AsNoTracking().SingleAsync(p => p.Email == DemoUser.Anna.Email, ct);
        var card = await db.Cards.AsNoTracking().Where(c => c.PersonId == anna.Id).Select(c => c.CardNumber).FirstOrDefaultAsync(ct);
        return (anna.Id.Value, card);
    }
}
