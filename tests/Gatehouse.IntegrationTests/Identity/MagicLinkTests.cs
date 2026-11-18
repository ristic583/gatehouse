extern alias GatehouseApi;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Gatehouse.Application.Identity.MagicLinks;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;
using Gatehouse.Infrastructure.Identity;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Gatehouse.IntegrationTests.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AuthenticationSetup = GatehouseApi::Gatehouse.Api.Auth.AuthenticationSetup;

namespace Gatehouse.IntegrationTests.Identity;

/// <summary>Magic links and guest sessions (M8-13); guests, their links and the test employee are removed afterwards.</summary>
public sealed class MagicLinkTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Tenant = "nordlys";
    private static readonly Uri Sessions = new("/api/v1/guest/sessions", UriKind.Relative);

    private readonly GatehouseApiFactory _api = new(postgres);
    private readonly List<PersonId> _people = [];

    public ValueTask InitializeAsync() => _api.InitializeAsync();

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    [Fact]
    public async Task Sponsor_issues_link_for_guest()
    {
        var ct = TestContext.Current.CancellationToken;
        var guest = await GuestAsync(ct);
        using var mikkel = _api.CreateClientFor(DemoUser.Mikkel);

        using var response = await mikkel.PostAsync(LinksOf(guest), null, ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<IssuedMagicLinkDto>(ApiJson.Options, ct))!;
        Assert.StartsWith("http://localhost:3000/guest?token=", body.Url, StringComparison.Ordinal);
        Assert.InRange(body.ExpiresAt - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(29), TimeSpan.FromMinutes(31));
        var token = TokenOf(body.Url);
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var link = await db.MagicLinks.AsNoTracking().SingleAsync(l => l.PersonId == guest, ct);
        Assert.Equal(64, link.TokenHash.Length);
        Assert.DoesNotContain(link.TokenHash, token, StringComparison.OrdinalIgnoreCase);
        Assert.True(await db.AuditEntries.AsNoTracking().AnyAsync(a => a.Action == "magic_link.issued" && a.SubjectId == guest.Value.ToString(), ct));
        using var anna = _api.CreateClientFor(DemoUser.Anna);
        using var stranger = await anna.PostAsync(LinksOf(guest), null, ct);
        Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);
    }

    [Fact]
    public async Task Link_for_employee_is_rejected()
    {
        var ct = TestContext.Current.CancellationToken;
        PersonId employee;
        await using (var scope = await _api.CreateTenantScopeAsync(Tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var mikkel = await db.Persons.AsNoTracking().SingleAsync(p => p.Email == DemoUser.Mikkel.Email, ct);
            var person = MappingData.Employee($"ml-{Guid.NewGuid():N}@nordlys.test");
            person.SetManager(mikkel.Id);
            person.ClearDomainEvents();
            db.Persons.Add(person);
            _people.Add(person.Id);
            await db.SaveChangesAsync(ct);
            employee = person.Id;
        }

        using var client = _api.CreateClientFor(DemoUser.Mikkel);
        using var response = await client.PostAsync(LinksOf(employee), null, ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("magic_link.guest_only", await response.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Link_redeems_once()
    {
        var ct = TestContext.Current.CancellationToken;
        var guest = await GuestAsync(ct);
        var token = await IssueAsync(guest, ct);
        using var anonymous = _api.CreateClient();

        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => anonymous.PostAsJsonAsync(Sessions, new { token }, ct)));
        using var again = await anonymous.PostAsJsonAsync(Sessions, new { token }, ct);

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Unauthorized);
        Assert.Equal(HttpStatusCode.Unauthorized, again.StatusCode);
        var session = (await results.Single(r => r.IsSuccessStatusCode).Content.ReadFromJsonAsync<GuestSessionDto>(ApiJson.Options, ct))!;
        Assert.Equal($"guest:{guest.Value}", session.Sub);
        Assert.Equal(Tenant, session.TenantId);
        Assert.False(string.IsNullOrEmpty(session.AccessToken));
        foreach (var r in results)
        {
            r.Dispose();
        }
    }

    [Fact]
    public async Task Link_older_than_30_minutes_is_refused()
    {
        var ct = TestContext.Current.CancellationToken;
        var guest = await GuestAsync(ct);
        var token = await IssueAsync(guest, ct);
        await using (var scope = await _api.CreateTenantScopeAsync(Tenant))
        {
            await scope.ServiceProvider.GetRequiredService<TenantDbContext>().MagicLinks.Where(l => l.PersonId == guest)
                .ExecuteUpdateAsync(s => s.SetProperty(l => l.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)), ct);
        }

        using var anonymous = _api.CreateClient();
        using var response = await anonymous.PostAsJsonAsync(Sessions, new { token }, ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("magic_link.invalid", await response.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_and_used_tokens_get_same_401()
    {
        var ct = TestContext.Current.CancellationToken;
        var guest = await GuestAsync(ct);
        var token = await IssueAsync(guest, ct);
        using var anonymous = _api.CreateClient();
        using (var first = await anonymous.PostAsJsonAsync(Sessions, new { token }, ct))
        {
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        }

        using var used = await anonymous.PostAsJsonAsync(Sessions, new { token }, ct);
        using var unknown = await anonymous.PostAsJsonAsync(Sessions, new { token = "AAAA.BBBB" }, ct);
        using var tampered = await anonymous.PostAsJsonAsync(Sessions, new { token = token[..^2] + "xx" }, ct);
        using var empty = await anonymous.PostAsJsonAsync(Sessions, new { }, ct);

        var expected = await ShapeAsync(used, ct);
        Assert.Equal(HttpStatusCode.Unauthorized, used.StatusCode);
        Assert.Contains("magic_link.invalid", expected, StringComparison.Ordinal);
        foreach (var other in new[] { unknown, tampered, empty })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, other.StatusCode);
            Assert.Equal(expected, await ShapeAsync(other, ct));
        }
    }

    [Fact]
    public async Task Guest_token_reaches_api_as_guest()
    {
        var ct = TestContext.Current.CancellationToken;
        var guest = await GuestAsync(ct);
        var token = await IssueAsync(guest, ct);
        GuestSessionDto session;
        using (var anonymous = _api.CreateClient())
        using (var response = await anonymous.PostAsJsonAsync(Sessions, new { token }, ct))
        {
            session = (await response.Content.ReadFromJsonAsync<GuestSessionDto>(ApiJson.Options, ct))!;
        }

        await using var real = RealAuth();
        using var client = real.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        using var me = await client.GetAsync(new Uri("/api/v1/me", UriKind.Relative), ct);
        using var cards = await client.GetAsync(new Uri("/api/v1/me/cards", UriKind.Relative), ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken[..^3] + "abc");
        using var forged = await client.GetAsync(new Uri("/api/v1/me", UriKind.Relative), ct);

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var body = JsonNode.Parse(await me.Content.ReadAsStringAsync(ct))!;
        Assert.Equal($"guest:{guest.Value}", body["subject"]!.GetValue<string>());
        Assert.Equal(Tenant, body["tenantId"]!.GetValue<string>());
        Assert.Equal(HttpStatusCode.OK, cards.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, forged.StatusCode);
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var linked = await scope.ServiceProvider.GetRequiredService<TenantDbContext>().Persons.AsNoTracking().SingleAsync(p => p.Id == guest, ct);
        Assert.Equal($"guest:{guest.Value}", linked.IdentitySubject);
    }

    [Fact]
    public async Task Guest_token_expires_after_30_minutes()
    {
        var ct = TestContext.Current.CancellationToken;
        var guestId = await GuestAsync(ct);
        Person guest;
        await using (var scope = await _api.CreateTenantScopeAsync(Tenant))
        {
            guest = await scope.ServiceProvider.GetRequiredService<TenantDbContext>().Persons.AsNoTracking().SingleAsync(p => p.Id == guestId, ct);
        }

        await using var real = RealAuth();
        var issuer = real.Services.GetRequiredService<IGuestTokenIssuer>();
        var stale = issuer.Issue(guest, new TenantId(Tenant), DateTimeOffset.UtcNow.AddMinutes(-31));
        var fresh = issuer.Issue(guest, new TenantId(Tenant), DateTimeOffset.UtcNow.AddMinutes(-29));
        using var client = real.CreateClient();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", stale.AccessToken);
        using var expired = await client.GetAsync(new Uri("/api/v1/me", UriKind.Relative), ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", fresh.AccessToken);
        using var valid = await client.GetAsync(new Uri("/api/v1/me", UriKind.Relative), ct);

        Assert.InRange(fresh.ExpiresAt - DateTimeOffset.UtcNow, TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(61));
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
    }

    [Fact]
    public async Task Redeem_is_rate_limited()
    {
        var ct = TestContext.Current.CancellationToken;
        using var anonymous = _api.CreateClient();
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 11; i++)
        {
            using var response = await anonymous.PostAsJsonAsync(Sessions, new { token = $"junk-{i}" }, ct);
            statuses.Add(response.StatusCode);
        }

        Assert.All(statuses.Take(10), s => Assert.Equal(HttpStatusCode.Unauthorized, s));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[10]);
    }

    private static Uri LinksOf(PersonId id) => new($"/api/v1/people/{id.Value}/magic-links", UriKind.Relative);

    private static string TokenOf(string url) => Uri.UnescapeDataString(url[(url.IndexOf("token=", StringComparison.Ordinal) + "token=".Length)..]);

    private static async Task<string> ShapeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var node = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!.AsObject();
        node.Remove("traceId");
        return node.ToJsonString();
    }

    private WebApplicationFactory<GatehouseApi::Program> RealAuth() => _api.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        s.PostConfigure<AuthenticationOptions>(o =>
        {
            o.DefaultScheme = AuthenticationSetup.DefaultScheme;
            o.DefaultAuthenticateScheme = AuthenticationSetup.DefaultScheme;
            o.DefaultChallengeScheme = AuthenticationSetup.DefaultScheme;
            o.DefaultForbidScheme = AuthenticationSetup.DefaultScheme;
        })));

    private async Task<string> IssueAsync(PersonId guest, CancellationToken ct)
    {
        using var mikkel = _api.CreateClientFor(DemoUser.Mikkel);
        using var response = await mikkel.PostAsync(LinksOf(guest), null, ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return TokenOf((await response.Content.ReadFromJsonAsync<IssuedMagicLinkDto>(ApiJson.Options, ct))!.Url);
    }

    private async Task<PersonId> GuestAsync(CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var sponsor = await db.Persons.AsNoTracking().SingleAsync(p => p.Email == DemoUser.Mikkel.Email, ct);
        var now = DateTimeOffset.UtcNow;
        var guest = Person.Create(PersonSource.Local, PersonType.Guest, "Gus", "Guest", $"guest-{Guid.NewGuid():N}@x.test",
            Eligibility.Create(now.AddHours(-1), now.AddHours(12), "Europe/Copenhagen").Value, null, sponsor, now).Value;
        guest.ClearDomainEvents();
        db.Persons.Add(guest);
        _people.Add(guest.Id);
        await db.SaveChangesAsync(ct);
        return guest.Id;
    }
}
