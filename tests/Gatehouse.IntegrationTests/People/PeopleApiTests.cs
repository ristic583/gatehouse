using System.Net;
using System.Net.Http.Json;
using Gatehouse.Application.People.Dtos;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.People;

/// <summary>People API (M8-07) on nordlys; onboarded people (email prefix <c>onb-</c>) and their cards are removed afterwards.</summary>
public sealed class PeopleApiTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Tenant = "nordlys";
    private static readonly Uri People = new("/api/v1/people", UriKind.Relative);

    private readonly GatehouseApiFactory _api = new(postgres);
    private readonly string _prefix = $"onb-{Guid.NewGuid():N}"[..16];

    public ValueTask InitializeAsync() => _api.InitializeAsync();

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    [Fact]
    public async Task Sponsor_sees_sponsored_people_only()
    {
        var ct = TestContext.Current.CancellationToken;
        var mikkelId = await PersonIdAsync(DemoUser.Mikkel, ct);
        using var mikkel = _api.CreateClientFor(DemoUser.Mikkel);

        var people = await mikkel.GetFromJsonAsync<List<PersonSummaryDto>>(new Uri("/api/v1/people?limit=200", UriKind.Relative), ApiJson.Options, ct);

        Assert.NotNull(people);
        Assert.Contains(people, p => p.Email == DemoUser.Oliver.Email && p.Sponsor?.PersonId == mikkelId);
        Assert.DoesNotContain(people, p => p.Email == DemoUser.Freja.Email);
        Assert.All(people, p => Assert.True(p.PersonId == mikkelId || p.Sponsor?.PersonId == mikkelId || p.Manager?.PersonId == mikkelId));
    }

    [Fact]
    public async Task Security_staff_detail_offers_card_and_revoke_actions()
    {
        var ct = TestContext.Current.CancellationToken;
        Guid annaId;
        await using (var scope = await _api.CreateTenantScopeAsync(Tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            annaId = (await db.Persons.AsNoTracking().SingleAsync(p => p.Email == DemoUser.Anna.Email, ct)).Id.Value;
        }

        using var jonas = _api.CreateClientFor(DemoUser.Jonas);
        var detail = await jonas.GetFromJsonAsync<PersonDetailDto>(new Uri($"/api/v1/people/{annaId}", UriKind.Relative), ApiJson.Options, ct);

        Assert.True(detail!.CanDeactivateCards);
        Assert.False(detail.CanTerminate);
        Assert.Contains(detail.Entitlements, e => e.Status == "Approved" && e.RevokeAction == "emergency-revoke");
        Assert.DoesNotContain(detail.Entitlements, e => e.Status != "Approved" && e.RevokeAction != null);
    }

    [Fact]
    public async Task Admin_sees_all_people()
    {
        var ct = TestContext.Current.CancellationToken;
        int total;
        await using (var scope = await _api.CreateTenantScopeAsync(Tenant))
        {
            total = await scope.ServiceProvider.GetRequiredService<TenantDbContext>().Persons.CountAsync(ct);
        }

        using var freja = _api.CreateClientFor(DemoUser.Freja);
        var people = await freja.GetFromJsonAsync<List<PersonSummaryDto>>(new Uri("/api/v1/people?limit=200", UriKind.Relative), ApiJson.Options, ct);
        var page = await freja.GetFromJsonAsync<List<PersonSummaryDto>>(People, ApiJson.Options, ct);

        Assert.Equal(Math.Min(total, 200), people!.Count);
        Assert.Equal(Math.Min(total, 50), page!.Count);
        Assert.Contains(people, p => p.Email == DemoUser.Jonas.Email);
    }

    [Fact]
    public async Task Name_query_filters_case_insensitively()
    {
        var ct = TestContext.Current.CancellationToken;
        using var freja = _api.CreateClientFor(DemoUser.Freja);

        // Found by the M10 floor-plan e2e: the name filter must translate to SQL (it used ToLowerInvariant → 500).
        var byGiven = await freja.GetFromJsonAsync<List<PersonSummaryDto>>(new Uri("/api/v1/people?type=Employee&query=JONAS", UriKind.Relative), ApiJson.Options, ct);
        var byFamily = await freja.GetFromJsonAsync<List<PersonSummaryDto>>(new Uri("/api/v1/people?query=nielsen", UriKind.Relative), ApiJson.Options, ct);

        Assert.Contains(byGiven!, p => p.Email == DemoUser.Jonas.Email);
        Assert.Contains(byFamily!, p => p.Email == DemoUser.Jonas.Email);
        Assert.DoesNotContain(byGiven!, p => p.Email == DemoUser.Freja.Email);
    }

    [Fact]
    public async Task Visitor_without_end_date_is_400()
    {
        var ct = TestContext.Current.CancellationToken;
        using var anna = _api.CreateClientFor(DemoUser.Anna);

        using var response = await anna.PostAsJsonAsync(People, new
        {
            type = "Visitor",
            givenName = "Vera",
            familyName = "Visit",
            email = $"{_prefix}-v@x.test",
            start = DateTimeOffset.UtcNow,
            timeZoneId = "Europe/Copenhagen",
        }, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Visitor_longer_than_cap_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        using var anna = _api.CreateClientFor(DemoUser.Anna);

        using var response = await anna.PostAsJsonAsync(People, Onboard("Visitor", "v2", days: 10), ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("person.sponsorship_too_long", await response.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Onboarding_sets_caller_as_sponsor_and_audits()
    {
        var ct = TestContext.Current.CancellationToken;
        var annaId = await PersonIdAsync(DemoUser.Anna, ct);
        using var anna = _api.CreateClientFor(DemoUser.Anna);
        using var oliver = _api.CreateClientFor(DemoUser.Oliver);

        using var forbidden = await oliver.PostAsJsonAsync(People, Onboard("External", "x0", days: 30), ct);
        using var otherSponsor = await anna.PostAsJsonAsync(People, Onboard("External", "x1", days: 30, sponsorId: Guid.NewGuid()), ct);
        using var response = await anna.PostAsJsonAsync(People, Onboard("External", "x2", days: 30), ct);
        using var duplicate = await anna.PostAsJsonAsync(People, Onboard("External", "x2", days: 30), ct);

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, otherSponsor.StatusCode);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<PersonDetailDto>(ApiJson.Options, ct);
        Assert.NotNull(dto);
        Assert.Equal(annaId, dto.Summary.Sponsor?.PersonId);
        Assert.Equal("Local", dto.Summary.Source);
        Assert.True(dto.CanManage);
        Assert.True(dto.CanActivateCards);
        Assert.False(dto.CanIssueMagicLink);
        Assert.Empty(dto.Cards);
        var detail = await anna.GetFromJsonAsync<PersonDetailDto>(new Uri($"/api/v1/people/{dto.Summary.PersonId}", UriKind.Relative), ApiJson.Options, ct);
        Assert.Equal(dto.Summary.Email, detail!.Summary.Email);

        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var subjectId = dto.Summary.PersonId.ToString();
        Assert.True(await db.AuditEntries.AsNoTracking().AnyAsync(a => a.Action == "person.onboarded" && a.SubjectId == subjectId, ct));
    }

    [Fact]
    public async Task Onboarding_with_card_assigns_card()
    {
        var ct = TestContext.Current.CancellationToken;
        using var anna = _api.CreateClientFor(DemoUser.Anna);
        var number = Random.Shared.NextInt64(100_000_000_000, 999_999_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);

        using var response = await anna.PostAsJsonAsync(People, Onboard("Guest", "g1", days: 5, cardNumber: number), ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<PersonDetailDto>(ApiJson.Options, ct);
        var card = Assert.Single(dto!.Cards);
        Assert.Equal("Assigned", card.State);
        Assert.DoesNotContain(number, card.CardNumberMasked, StringComparison.Ordinal);
        Assert.True(dto.CanIssueMagicLink);
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var cardId = card.CardId.ToString();
        Assert.True(await db.AuditEntries.AsNoTracking().AnyAsync(a => a.Action == "card.assigned" && a.SubjectId == cardId, ct));
    }

    private object Onboard(string type, string tag, int days, Guid? sponsorId = null, string? cardNumber = null)
    {
        var start = DateTimeOffset.UtcNow.Date;
        return new
        {
            type,
            givenName = "Test",
            familyName = tag,
            email = $"{_prefix}-{tag}@x.test",
            start = new DateTimeOffset(start, TimeSpan.Zero),
            end = new DateTimeOffset(start.AddDays(days), TimeSpan.Zero),
            timeZoneId = "Europe/Copenhagen",
            sponsorId,
            cardNumber,
        };
    }

    private async Task<Guid> PersonIdAsync(DemoUser user, CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        return (await db.Persons.AsNoTracking().SingleAsync(p => p.Email == user.Email, ct)).Id.Value;
    }
}
