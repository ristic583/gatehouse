using System.Globalization;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Credentials;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.People;

/// <summary>Test people of nordlys with grants on HQ-Copenhagen (Connector mode) and cards (in the factory's own tenant database copy).</summary>
internal sealed class LifecycleArrange(GatehouseApiFactory api)
{
    public const string Tenant = "nordlys";

    private readonly List<PersonId> _people = [];
    private PacsInstanceId _hq;
    private Domain.Sites.AreaId[] _areas = [];

    public PacsInstanceId Hq => _hq;

    public async Task InitializeAsync()
    {
        await using var scope = await api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var hq = await db.PacsInstances.AsNoTracking().SingleAsync(p => p.Name == "HQ-Copenhagen");
        _hq = hq.Id;
        _areas = await db.Areas.AsNoTracking().Where(a => a.Code == "CPH-VAULT" || a.Code == "CPH-SERVER").Select(a => a.Id).ToArrayAsync();
        await db.PacsInstances.Where(p => p.Id == _hq).ExecuteUpdateAsync(s => s.SetProperty(p => p.FulfillmentMode, FulfillmentMode.Connector));
    }

    /// <summary>An External sponsored by mikkel.holm with the given eligibility window.</summary>
    public async Task<PersonId> ExternalAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        await using var scope = await api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var sponsor = await db.Persons.AsNoTracking().SingleAsync(p => p.Email == DemoUser.Mikkel.Email, ct);
        var person = Person.Create(PersonSource.Local, PersonType.External, "Life", "Window", $"lc-{Guid.NewGuid():N}@x.test",
            Eligibility.Create(start, end, "Europe/Copenhagen").Value, null, sponsor, DateTimeOffset.UtcNow).Value;
        person.ClearDomainEvents();
        db.Persons.Add(person);
        _people.Add(person.Id);
        await db.SaveChangesAsync(ct);
        return person.Id;
    }

    /// <summary>An Approved entitlement on CPH-VAULT (no fulfillments).</summary>
    public async Task<EntitlementId> ApprovedAsync(PersonId personId, DateTimeOffset start, DateTimeOffset effectiveEnd, CancellationToken ct, bool confirmedGrant = false)
    {
        await using var scope = await api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var vault = await db.Areas.AsNoTracking().SingleAsync(a => a.Code == "CPH-VAULT", ct);
        var entitlement = Entitlement.Create(personId, vault.Id, personId, start, effectiveEnd, effectiveEnd, "Lifecycle window test",
            [new StepPlan(ApproverKind.AreaOwner, null)], start);
        entitlement.ClearDomainEvents();
        db.Add(entitlement);
        db.Entry(entitlement).Property(e => e.Status).CurrentValue = EntitlementStatus.Approved;
        if (confirmedGrant)
        {
            var grant = Domain.Fulfillment.Fulfillment.Create(entitlement.Id, _hq, FulfillmentKind.Grant, CommandPriority.Normal, start);
            grant.ClearDomainEvents();
            db.Add(grant);
            db.Entry(grant).Property(f => f.Status).CurrentValue = FulfillmentStatus.Confirmed;
        }

        await db.SaveChangesAsync(ct);
        return entitlement.Id;
    }

    /// <summary>An External sponsored by <paramref name="sponsor"/> (30 days) with an Active card and one Approved entitlement per area, each with a Confirmed grant on HQ.</summary>
    public async Task<(PersonId PersonId, CredentialId CardId, string Email)> SponsoredAsync(DemoUser sponsor, int grants, CancellationToken ct, DateTimeOffset? eligibilityEnd = null)
    {
        await using var scope = await api.CreateTenantScopeAsync(Tenant);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var now = DateTimeOffset.UtcNow;
        var sponsorPerson = await db.Persons.AsNoTracking().SingleAsync(p => p.Email == sponsor.Email, ct);
        var email = $"lc-{Guid.NewGuid():N}@x.test";
        var person = Person.Create(PersonSource.Local, PersonType.External, "Life", "Cycle", email,
            Eligibility.Create(now.AddDays(-10), eligibilityEnd ?? now.AddDays(30), "Europe/Copenhagen").Value, null, sponsorPerson, now).Value;
        person.ClearDomainEvents();
        db.Persons.Add(person);
        _people.Add(person.Id);
        foreach (var areaId in _areas.Take(grants))
        {
            var entitlement = Entitlement.Create(person.Id, areaId, person.Id, now.AddDays(-1), now.AddDays(20), now.AddDays(20), "Lifecycle test",
                [new StepPlan(ApproverKind.AreaOwner, null)], now);
            var grant = Domain.Fulfillment.Fulfillment.Create(entitlement.Id, _hq, FulfillmentKind.Grant, CommandPriority.Normal, now);
            entitlement.ClearDomainEvents();
            grant.ClearDomainEvents();
            db.AddRange(entitlement, grant);
            db.Entry(entitlement).Property(e => e.Status).CurrentValue = EntitlementStatus.Approved;
            db.Entry(grant).Property(f => f.Status).CurrentValue = FulfillmentStatus.Confirmed;
        }

        var card = Card.CreateActive(person.Id, Random.Shared.NextInt64(100_000_000_000, 999_999_999_999).ToString(CultureInfo.InvariantCulture), now).Value;
        db.Cards.Add(card);
        await db.SaveChangesAsync(ct);
        return (person.Id, card.Id, email);
    }
}
