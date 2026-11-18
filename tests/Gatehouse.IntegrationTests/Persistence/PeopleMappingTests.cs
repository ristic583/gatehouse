using Gatehouse.Domain.People;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.IntegrationTests.Persistence;

public sealed class PeopleMappingTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Person_with_eligibility_round_trips()
    {
        var ct = TestContext.Current.CancellationToken;
        var testDb = await TenantTestDb.CreateAsync(postgres, ct);
        var sponsor = MappingData.Employee("sponsor@x.test");
        var eligibility = Eligibility.Create(MappingData.Now, MappingData.Now.AddDays(180), "Europe/Copenhagen").Value;
        var external = Person.Create(PersonSource.Local, PersonType.External, "Oliver", "Kruse", "oliver@x.test", eligibility, null, sponsor, MappingData.Now).Value;
        external.LinkIdentity("sub-oliver");

        await using (var db = testDb.NewContext())
        {
            db.Persons.AddRange(sponsor, external);
            await db.SaveChangesAsync(ct);
        }

        await using (var db = testDb.NewContext())
        {
            var loaded = await db.Persons.AsNoTracking().SingleAsync(p => p.Id == external.Id, ct);
            Assert.Equal(eligibility, loaded.Eligibility);
            Assert.Equal(sponsor.Id, loaded.SponsorId);
            Assert.Equal(PersonType.External, loaded.Type);
            Assert.Equal("sub-oliver", loaded.IdentitySubject);
        }
    }

    [Fact]
    public async Task Duplicate_email_violates_unique_index()
    {
        var ct = TestContext.Current.CancellationToken;
        var testDb = await TenantTestDb.CreateAsync(postgres, ct);
        await using var db = testDb.NewContext();
        db.Persons.AddRange(MappingData.Employee("same@x.test"), MappingData.Employee("SAME@x.test"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
    }

    [Fact]
    public async Task Role_assignment_round_trips()
    {
        var ct = TestContext.Current.CancellationToken;
        var testDb = await TenantTestDb.CreateAsync(postgres, ct);
        var person = MappingData.Employee("staff@x.test");
        RoleAssignment assignment;

        await using (var db = testDb.NewContext())
        {
            var (site, _, _) = MappingData.Building(db);
            db.Persons.Add(person);
            assignment = RoleAssignment.Create(person.Id, AssignedRole.SecurityStaff, site.Id).Value;
            db.RoleAssignments.Add(assignment);
            await db.SaveChangesAsync(ct);
        }

        await using (var db = testDb.NewContext())
        {
            var loaded = await db.RoleAssignments.AsNoTracking().SingleAsync(r => r.PersonId == person.Id, ct);
            Assert.Equal(AssignedRole.SecurityStaff, loaded.Role);
            Assert.Equal(assignment.SiteId, loaded.SiteId);
        }
    }
}
