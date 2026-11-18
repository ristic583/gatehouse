using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Gatehouse.Application.Credentials.Dtos;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Credentials;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Credentials;

/// <summary>Card API (M8-06) on test people of nordlys.</summary>
public sealed class CardApiTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Tenant = "nordlys";

    private readonly GatehouseApiFactory _api = new(postgres);
    private readonly List<PersonId> _people = [];
    private PacsInstanceId _hq;
    private FulfillmentMode _hqMode;
    private Domain.Sites.AreaId _areaId;

    public async ValueTask InitializeAsync()
    {
        await _api.InitializeAsync();
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var hq = await db.PacsInstances.AsNoTracking().SingleAsync(p => p.Name == "HQ-Copenhagen");
        (_hq, _hqMode) = (hq.Id, hq.FulfillmentMode);
        _areaId = (await db.Areas.AsNoTracking().SingleAsync(a => a.Code == "CPH-VAULT")).Id;
        await db.PacsInstances.Where(p => p.Id == _hq).ExecuteUpdateAsync(s => s.SetProperty(p => p.FulfillmentMode, FulfillmentMode.Connector));
    }

    public ValueTask DisposeAsync() => _api.DisposeAsync();

    [Fact]
    public async Task Employee_activates_own_card()
    {
        var ct = TestContext.Current.CancellationToken;
        var (person, cardId) = await ArrangeAsync(PersonType.Employee, ct);
        using var client = ClientFor(person);

        using var response = await client.PostAsync(new Uri($"/api/v1/cards/{cardId.Value}/activate", UriKind.Relative), null, ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<CardDto>(ApiJson.Options, ct);
        Assert.NotNull(dto);
        Assert.Equal("Active", dto.State);
        Assert.Null(dto.PacsStatus);
        var mine = await client.GetFromJsonAsync<List<CardDto>>(new Uri("/api/v1/me/cards", UriKind.Relative), ApiJson.Options, ct);
        Assert.Equal(cardId.Value, Assert.Single(mine!).CardId);
        Assert.DoesNotContain(person.CardNumberForTest, Assert.Single(mine!).CardNumberMasked, StringComparison.Ordinal);
    }

    [Fact]
    public async Task External_cannot_activate_own_card()
    {
        var ct = TestContext.Current.CancellationToken;
        var (person, cardId) = await ArrangeAsync(PersonType.External, ct);
        using var client = ClientFor(person);

        using var response = await client.PostAsync(new Uri($"/api/v1/cards/{cardId.Value}/activate", UriKind.Relative), null, ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains("auth.forbidden", await response.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
        Assert.Equal(CardState.Assigned, await CardStateAsync(cardId, ct));
    }

    [Fact]
    public async Task Sponsor_activates_card_of_sponsored_person()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, cardId) = await ArrangeAsync(PersonType.External, ct);
        using var mikkel = _api.CreateClientFor(DemoUser.Mikkel);

        using var response = await mikkel.PostAsync(new Uri($"/api/v1/cards/{cardId.Value}/activate", UriKind.Relative), null, ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(CardState.Active, await CardStateAsync(cardId, ct));
    }

    [Fact]
    public async Task Manager_activates_team_card()
    {
        var ct = TestContext.Current.CancellationToken;
        var (_, cardId) = await ArrangeAsync(PersonType.Employee, ct);
        using var mikkel = _api.CreateClientFor(DemoUser.Mikkel);
        using var anna = _api.CreateClientFor(DemoUser.Anna);

        using var stranger = await anna.PostAsync(new Uri($"/api/v1/cards/{cardId.Value}/activate", UriKind.Relative), null, ct);
        using var response = await mikkel.PostAsync(new Uri($"/api/v1/cards/{cardId.Value}/activate", UriKind.Relative), null, ct);

        Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(CardState.Active, await CardStateAsync(cardId, ct));
    }

    [Fact]
    public async Task Admin_assigns_card_but_cannot_activate()
    {
        var ct = TestContext.Current.CancellationToken;
        var person = await ArrangePersonAsync(PersonType.Employee, ct);
        using var freja = _api.CreateClientFor(DemoUser.Freja);

        using var assigned = await freja.PostAsJsonAsync(new Uri($"/api/v1/people/{person.Id.Value}/cards", UriKind.Relative), new { cardNumber = NewNumber() }, ct);
        Assert.Equal(HttpStatusCode.Created, assigned.StatusCode);
        var dto = await assigned.Content.ReadFromJsonAsync<CardDto>(ApiJson.Options, ct);
        Assert.Equal("Assigned", dto!.State);

        using var activate = await freja.PostAsync(new Uri($"/api/v1/cards/{dto.CardId}/activate", UriKind.Relative), null, ct);
        Assert.Equal(HttpStatusCode.Forbidden, activate.StatusCode);

        using var deactivate = await freja.PostAsJsonAsync(new Uri($"/api/v1/cards/{dto.CardId}/deactivate", UriKind.Relative), new { reason = "Lost" }, ct);
        Assert.Equal(HttpStatusCode.Conflict, deactivate.StatusCode);
        var cards = await freja.GetFromJsonAsync<List<CardDto>>(new Uri($"/api/v1/people/{person.Id.Value}/cards", UriKind.Relative), ApiJson.Options, ct);
        Assert.Equal(dto.CardId, Assert.Single(cards!).CardId);
    }

    [Fact]
    public async Task Activation_leaves_entitlements_untouched()
    {
        var ct = TestContext.Current.CancellationToken;
        var (person, cardId) = await ArrangeAsync(PersonType.Employee, ct, grantOnHq: true);
        var before = await EntitlementSnapshotAsync(person.Id, ct);
        using var client = ClientFor(person);

        using var response = await client.PostAsync(new Uri($"/api/v1/cards/{cardId.Value}/activate", UriKind.Relative), null, ct);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<CardDto>(ApiJson.Options, ct);
        Assert.Equal("ActivationPending", dto!.State);
        Assert.Equal("Pending", dto.PacsStatus);
        Assert.Equal(before, await EntitlementSnapshotAsync(person.Id, ct));

        using var deactivate = await client.PostAsJsonAsync(new Uri($"/api/v1/cards/{cardId.Value}/deactivate", UriKind.Relative), new { reason = "Lost" }, ct);
        Assert.Equal(HttpStatusCode.Conflict, deactivate.StatusCode);
        Assert.Equal(before, await EntitlementSnapshotAsync(person.Id, ct));
    }

    [Fact]
    public async Task Second_card_is_conflict()
    {
        var ct = TestContext.Current.CancellationToken;
        var (person, _) = await ArrangeAsync(PersonType.Employee, ct);
        var other = await ArrangePersonAsync(PersonType.Employee, ct);
        using var freja = _api.CreateClientFor(DemoUser.Freja);

        using var second = await freja.PostAsJsonAsync(new Uri($"/api/v1/people/{person.Id.Value}/cards", UriKind.Relative), new { cardNumber = NewNumber() }, ct);
        using var taken = await freja.PostAsJsonAsync(new Uri($"/api/v1/people/{other.Id.Value}/cards", UriKind.Relative), new { cardNumber = person.CardNumberForTest }, ct);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("card.already_assigned", await second.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        Assert.Contains("card.number_taken", await taken.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Security_staff_deactivates_card()
    {
        var ct = TestContext.Current.CancellationToken;
        var person = await ArrangePersonAsync(PersonType.Employee, ct);
        CredentialId cardId;
        await using (var scope = await _api.CreateTenantScopeAsync(Tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var now = DateTimeOffset.UtcNow;
            var card = Card.CreateActive(person.Id, NewNumber(), now).Value;
            card.ClearDomainEvents();
            db.Cards.Add(card);
            var entitlement = Entitlement.Create(person.Id, _areaId, person.Id, now.AddDays(-1), now.AddDays(30), now.AddDays(30), "Staff scope",
                [new StepPlan(ApproverKind.AreaOwner, null)], now);
            entitlement.ClearDomainEvents();
            db.Add(entitlement);
            db.Entry(entitlement).Property(e => e.Status).CurrentValue = EntitlementStatus.Approved;
            await db.SaveChangesAsync(ct);
            cardId = card.Id;
        }

        using var jonas = _api.CreateClientFor(DemoUser.Jonas);
        using var response = await jonas.PostAsJsonAsync(new Uri($"/api/v1/cards/{cardId.Value}/deactivate", UriKind.Relative), new { reason = "Reported lost" }, ct);

        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}");
        Assert.NotEqual(CardState.Active, await CardStateAsync(cardId, ct));
    }

    [Fact]
    public async Task Issuing_card_retries_grant_failed_for_missing_card()
    {
        var ct = TestContext.Current.CancellationToken;
        var person = await ArrangePersonAsync(PersonType.Employee, ct);
        FulfillmentId grantId;
        await using (var scope = await _api.CreateTenantScopeAsync(Tenant))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var now = DateTimeOffset.UtcNow;
            var entitlement = Entitlement.Create(person.Id, _areaId, person.Id, now.AddDays(-1), now.AddDays(30), now.AddDays(30), "Card retry test",
                [new StepPlan(ApproverKind.AreaOwner, null)], now);
            var grant = Domain.Fulfillment.Fulfillment.Create(entitlement.Id, _hq, FulfillmentKind.Grant, CommandPriority.Normal, now);
            entitlement.ClearDomainEvents();
            grant.ClearDomainEvents();
            db.AddRange(entitlement, grant);
            db.Entry(entitlement).Property(e => e.Status).CurrentValue = EntitlementStatus.Approved;
            db.Entry(grant).Property(f => f.Status).CurrentValue = FulfillmentStatus.Failed;
            db.Entry(grant).Property(f => f.LastError).CurrentValue = "fulfillment.no_active_card: The person has no card to provision.";
            await db.SaveChangesAsync(ct);
            grantId = grant.Id;
        }

        using var freja = _api.CreateClientFor(DemoUser.Freja);
        using var assigned = await freja.PostAsJsonAsync(new Uri($"/api/v1/people/{person.Id.Value}/cards", UriKind.Relative), new { cardNumber = NewNumber() }, ct);

        Assert.Equal(HttpStatusCode.Created, assigned.StatusCode);
        await using var check = await _api.CreateTenantScopeAsync(Tenant);
        var read = check.ServiceProvider.GetRequiredService<TenantDbContext>();
        Assert.NotEqual(FulfillmentStatus.Failed, (await read.Fulfillments.AsNoTracking().SingleAsync(f => f.Id == grantId, ct)).Status);
    }

    private static string NewNumber() => Random.Shared.NextInt64(100_000_000_000, 999_999_999_999).ToString(CultureInfo.InvariantCulture);

    private HttpClient ClientFor(TestPerson person) => _api.CreateClientFor("test-card-" + person.Id.Value, person.Email, Tenant);

    private async Task<TestPerson> ArrangePersonAsync(PersonType type, CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var now = DateTimeOffset.UtcNow;
        var mikkel = await db.Persons.AsNoTracking().SingleAsync(p => p.Email == DemoUser.Mikkel.Email, ct);
        var email = $"card-{Guid.NewGuid():N}@x.test";
        var person = type == PersonType.Employee
            ? Person.Create(PersonSource.Authoritative, type, "Card", "Tester", email, Eligibility.Create(now.AddYears(-1), null, "Europe/Copenhagen").Value, mikkel.Id, null, now).Value
            : Person.Create(PersonSource.Local, type, "Card", "Guest", email, Eligibility.Create(now.AddDays(-1), now.AddDays(30), "Europe/Copenhagen").Value, null, mikkel, now).Value;
        person.ClearDomainEvents();
        db.Persons.Add(person);
        _people.Add(person.Id);
        await db.SaveChangesAsync(ct);
        return new TestPerson(person.Id, email, string.Empty);
    }

    // A person (manager or sponsor: mikkel.holm) with an Assigned card, optionally with a Confirmed grant on HQ-Copenhagen.
    private async Task<(TestPerson Person, CredentialId CardId)> ArrangeAsync(PersonType type, CancellationToken ct, bool grantOnHq = false)
    {
        var person = await ArrangePersonAsync(type, ct);
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var now = DateTimeOffset.UtcNow;
        if (grantOnHq)
        {
            var entitlement = Entitlement.Create(person.Id, _areaId, person.Id, now.AddDays(-1), now.AddDays(30), now.AddDays(30), "Card API test",
                [new StepPlan(ApproverKind.AreaOwner, null)], now);
            var grant = Domain.Fulfillment.Fulfillment.Create(entitlement.Id, _hq, FulfillmentKind.Grant, CommandPriority.Normal, now);
            entitlement.ClearDomainEvents();
            grant.ClearDomainEvents();
            db.AddRange(entitlement, grant);
            db.Entry(entitlement).Property(e => e.Status).CurrentValue = EntitlementStatus.Approved;
            db.Entry(grant).Property(f => f.Status).CurrentValue = FulfillmentStatus.Confirmed;
        }

        var number = NewNumber();
        var card = Card.Create(person.Id, number, now).Value;
        card.ClearDomainEvents();
        db.Cards.Add(card);
        await db.SaveChangesAsync(ct);
        return (person with { CardNumberForTest = number }, card.Id);
    }

    private async Task<CardState> CardStateAsync(CredentialId cardId, CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        return (await db.Cards.AsNoTracking().SingleAsync(c => c.Id == cardId, ct)).State;
    }

    private async Task<string> EntitlementSnapshotAsync(PersonId personId, CancellationToken ct)
    {
        await using var scope = await _api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var entitlements = await db.Entitlements.AsNoTracking().Where(e => e.PersonId == personId).Select(e => new { e.Id, e.Status, e.Version, e.EffectiveEnd }).ToListAsync(ct);
        var ids = entitlements.Select(e => e.Id).ToList();
        var fulfillments = await db.Fulfillments.AsNoTracking().Where(f => ids.Contains(f.EntitlementId)).Select(f => new { f.Id, f.Status, f.Kind }).ToListAsync(ct);
        return string.Join('|', entitlements.Select(e => $"{e.Id.Value}:{e.Status}:{e.Version}:{e.EffectiveEnd:O}"))
            + "#" + string.Join('|', fulfillments.OrderBy(f => f.Id.Value).Select(f => $"{f.Id.Value}:{f.Status}:{f.Kind}"));
    }

    private sealed record TestPerson(PersonId Id, string Email, string CardNumberForTest);
}
