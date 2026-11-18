using Gatehouse.Domain.Credentials;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.IntegrationTests.Persistence;

public sealed class CardMappingTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Card_round_trips()
    {
        var ct = TestContext.Current.CancellationToken;
        var testDb = await TenantTestDb.CreateAsync(postgres, ct);
        await using var db = testDb.NewContext();
        var person = MappingData.Employee("card1@example.com");
        db.Persons.Add(person);
        var card = Card.Create(person.Id, "100200300", MappingData.Now).Value;
        db.Cards.Add(card);
        await db.SaveChangesAsync(ct);

        await using var read = testDb.NewContext();
        var loaded = await read.Cards.AsNoTracking().SingleAsync(c => c.Id == card.Id, ct);
        Assert.Equal("100200300", loaded.CardNumber);
        Assert.Equal(CardState.Assigned, loaded.State);
        Assert.Equal(person.Id, loaded.PersonId);
    }

    [Fact]
    public async Task Duplicate_card_number_violates_unique_index()
    {
        var ct = TestContext.Current.CancellationToken;
        var testDb = await TenantTestDb.CreateAsync(postgres, ct);
        await using var db = testDb.NewContext();
        var person = MappingData.Employee("card2@example.com");
        db.Persons.Add(person);
        db.Cards.Add(Card.Create(person.Id, "555666777", MappingData.Now).Value);
        db.Cards.Add(Card.Create(person.Id, "555666777", MappingData.Now).Value);
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
    }
}
