using System.Net;
using System.Net.Http.Json;
using Gatehouse.Application.People.Dtos;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Credentials;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.People;

public sealed class SponsorshipTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatehouseApiFactory _api = new(postgres);
    private LifecycleArrange _arrange = null!;

    public async ValueTask InitializeAsync()
    {
        await _api.InitializeAsync();
        _arrange = new LifecycleArrange(_api);
        await _arrange.InitializeAsync();
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    [Fact]
    public async Task Ending_sponsorship_terminates_now()
    {
        var ct = TestContext.Current.CancellationToken;
        var (personId, cardId, _) = await _arrange.SponsoredAsync(DemoUser.Mikkel, grants: 1, ct);
        using var mikkel = _api.CreateClientFor(DemoUser.Mikkel);

        using var response = await mikkel.PostAsync(new Uri($"/api/v1/people/{personId.Value}/sponsorship/end", UriKind.Relative), null, ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<PersonDetailDto>(ApiJson.Options, ct);
        Assert.Equal("Terminated", dto!.Summary.Status);
        Assert.True(dto.Summary.EligibilityEnd <= DateTimeOffset.UtcNow);
        Assert.Equal("Revoked", Assert.Single(dto.Cards).State);
        Assert.Equal("Revoked", Assert.Single(dto.Entitlements).Status);
        await using var scope = await _api.CreateTenantScopeAsync(LifecycleArrange.Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        Assert.Equal(CardState.Revoked, (await db.Cards.AsNoTracking().SingleAsync(c => c.Id == cardId, ct)).State);
        Assert.Equal(EntitlementStatus.Revoked, (await db.Entitlements.AsNoTracking().SingleAsync(e => e.PersonId == personId, ct)).Status);
        var subject = personId.Value.ToString();
        Assert.True(await db.AuditEntries.AsNoTracking().AnyAsync(a => a.Action == "sponsorship.ended" && a.SubjectId == subject, ct));
        Assert.True(await db.AuditEntries.AsNoTracking().AnyAsync(a => a.Action == "person.terminated" && a.SubjectId == subject, ct));

        using var again = await mikkel.PostAsync(new Uri($"/api/v1/people/{personId.Value}/sponsorship/end", UriKind.Relative), null, ct);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Extending_beyond_cap_is_400()
    {
        var ct = TestContext.Current.CancellationToken;
        var (personId, _, _) = await _arrange.SponsoredAsync(DemoUser.Mikkel, grants: 0, ct);
        using var mikkel = _api.CreateClientFor(DemoUser.Mikkel);
        var url = new Uri($"/api/v1/people/{personId.Value}/eligibility", UriKind.Relative);

        using var tooLong = await mikkel.PutAsJsonAsync(url, new { end = DateTimeOffset.UtcNow.AddDays(400) }, ct);
        using var ok = await mikkel.PutAsJsonAsync(url, new { end = DateTimeOffset.UtcNow.AddDays(60) }, ct);

        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Contains("person.sponsorship_too_long", await tooLong.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var dto = await ok.Content.ReadFromJsonAsync<PersonDetailDto>(ApiJson.Options, ct);
        Assert.True(dto!.Summary.EligibilityEnd > DateTimeOffset.UtcNow.AddDays(59));
    }

    [Fact]
    public async Task Non_sponsor_cannot_end_sponsorship()
    {
        var ct = TestContext.Current.CancellationToken;
        var (personId, _, email) = await _arrange.SponsoredAsync(DemoUser.Mikkel, grants: 0, ct);
        var url = new Uri($"/api/v1/people/{personId.Value}/sponsorship/end", UriKind.Relative);
        using var anna = _api.CreateClientFor(DemoUser.Anna);
        using var jonas = _api.CreateClientFor(DemoUser.Jonas);
        using var self = _api.CreateClientFor("test-lc-" + personId.Value, email, LifecycleArrange.Tenant);

        using var stranger = await anna.PostAsync(url, null, ct);
        using var staff = await jonas.PostAsync(url, null, ct);
        using var own = await self.PostAsync(url, null, ct);

        Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, staff.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, own.StatusCode);
        await using var scope = await _api.CreateTenantScopeAsync(LifecycleArrange.Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        Assert.Equal(Domain.People.PersonStatus.Active, (await db.Persons.AsNoTracking().SingleAsync(p => p.Id == personId, ct)).Status);
    }
}
