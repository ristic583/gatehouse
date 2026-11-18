using Gatehouse.Application.People.Lifecycle;
using Gatehouse.Domain.Credentials;
using Gatehouse.Domain.Identity;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Tenancy;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Gatehouse.IntegrationTests.Persistence;

public sealed class LifecyclePersistenceTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Pin_credential_round_trips_ciphertext()
    {
        var ct = TestContext.Current.CancellationToken;
        var testDb = await TenantTestDb.CreateAsync(postgres, ct);
        await using (var db = testDb.NewContext())
        {
            var person = MappingData.Employee("pin1@example.com");
            db.Persons.Add(person);
            db.PinCredentials.Add(PinCredential.Set(person.Id, [9, 8, 7, 6], "local-v1", person.Id, MappingData.Now));
            await db.SaveChangesAsync(ct);
        }

        await using var read = testDb.NewContext();
        var loaded = await read.PinCredentials.AsNoTracking().SingleAsync(ct);
        Assert.Equal([9, 8, 7, 6], loaded.Ciphertext);
        Assert.Equal("local-v1", loaded.KeyId);
        Assert.Equal(MappingData.Now, loaded.SetAt);
    }

    [Fact]
    public async Task Second_active_card_for_person_is_rejected_by_index()
    {
        var ct = TestContext.Current.CancellationToken;
        var testDb = await TenantTestDb.CreateAsync(postgres, ct);
        await using var db = testDb.NewContext();
        var person = MappingData.Employee("cards@example.com");
        db.Persons.Add(person);
        var old = Card.Create(person.Id, "700800900", MappingData.Now).Value;
        old.Revoke("Lost", MappingData.Now);
        db.Cards.Add(old);
        db.Cards.Add(Card.Create(person.Id, "700800900", MappingData.Now).Value);
        await db.SaveChangesAsync(ct);

        db.Cards.Add(Card.Create(person.Id, "700800901", MappingData.Now).Value);

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task Magic_link_token_hash_is_unique()
    {
        var ct = TestContext.Current.CancellationToken;
        var testDb = await TenantTestDb.CreateAsync(postgres, ct);
        await using var db = testDb.NewContext();
        var person = MappingData.Employee("guest-link@example.com");
        db.Persons.Add(person);
        var hash = new string('b', 64);
        db.MagicLinks.Add(MagicLink.Issue(person.Id, hash, person.Id, MappingData.Now, TimeSpan.FromMinutes(30)));
        await db.SaveChangesAsync(ct);

        db.MagicLinks.Add(MagicLink.Issue(person.Id, hash, person.Id, MappingData.Now, TimeSpan.FromMinutes(30)));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task Settings_store_falls_back_to_defaults()
    {
        var ct = TestContext.Current.CancellationToken;
        var testDb = await TenantTestDb.CreateAsync(postgres, ct);
        await using var db = testDb.NewContext();
        db.TenantSettings.Add(new TenantSetting(LifecycleSettings.MaxVisitorDaysKey, "3", MappingData.Now));
        db.TenantSettings.Add(new TenantSetting(LifecycleSettings.RenewalGuestDaysKey, "not-a-number", MappingData.Now));
        await db.SaveChangesAsync(ct);
        var store = new LifecycleSettingsStore(db, NullLogger<LifecycleSettingsStore>.Instance);

        var settings = await store.GetAsync(ct);
        var invalid = await store.SetAsync("lifecycle.unknown", 5, MappingData.Now, ct);
        var set = await store.SetAsync(LifecycleSettings.MaxSponsorshipDaysKey, 180, MappingData.Now, ct);
        await db.SaveChangesAsync(ct);

        Assert.Equal(3, settings.MaxVisitorDays);
        Assert.Equal(LifecycleSettings.Defaults.RenewalGuestDays, settings.RenewalGuestDays);
        Assert.Equal(LifecycleSettings.Defaults.MaxSponsorshipDays, settings.MaxSponsorshipDays);
        Assert.Equal("lifecycle.invalid_setting", invalid.Error.Code);
        Assert.True(set.IsSuccess);
        Assert.Equal(180, (await store.GetAsync(ct)).MaxSponsorshipDays);
    }
}
