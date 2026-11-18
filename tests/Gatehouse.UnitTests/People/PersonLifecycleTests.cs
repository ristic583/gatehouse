using Gatehouse.Domain.People;
using Gatehouse.Domain.People.Events;

namespace Gatehouse.UnitTests.People;

public sealed class PersonLifecycleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 8, 0, 0, TimeSpan.Zero);

    private static Eligibility Window(DateTimeOffset start, DateTimeOffset? end) => Eligibility.Create(start, end, "Europe/Copenhagen").Value;

    private static Person Employee() =>
        Person.Create(PersonSource.Authoritative, PersonType.Employee, "Mikkel", "Holm", "mikkel@nordlys.test", Window(Now.AddYears(-2), null), null, null, Now).Value;

    private static Person Local(PersonType type, Person sponsor, int days) =>
        Person.Create(PersonSource.Local, type, "Vera", "Visit", $"{type}@x.test", Window(Now, Now.AddDays(days)), null, sponsor, Now).Value;

    [Fact]
    public void Hr_update_raises_eligibility_changed_only_when_dates_change()
    {
        var person = Employee();
        person.ClearDomainEvents();

        var renamed = person.UpdateFromHr("Mikkel", "Holm-Berg", null, Window(person.Eligibility.Start, null), Now);
        Assert.True(renamed.Value);
        Assert.Equal(["FamilyName"], Assert.Single(person.DomainEvents.OfType<PersonUpdated>()).ChangedFields);
        Assert.Empty(person.DomainEvents.OfType<EligibilityChanged>());

        person.ClearDomainEvents();
        var ended = person.UpdateFromHr("Mikkel", "Holm-Berg", null, Window(person.Eligibility.Start, Now.AddDays(10)), Now);
        Assert.True(ended.Value);
        var changed = Assert.Single(person.DomainEvents.OfType<EligibilityChanged>());
        Assert.Equal(Now.AddDays(10), changed.End);
        Assert.Null(changed.PreviousEnd);

        person.ClearDomainEvents();
        Assert.False(person.UpdateFromHr("Mikkel", "Holm-Berg", null, Window(person.Eligibility.Start, Now.AddDays(10)), Now).Value);
        Assert.Empty(person.DomainEvents);
    }

    [Fact]
    public void Hr_update_rejects_local_person()
    {
        var visitor = Local(PersonType.Visitor, Employee(), 3);

        var result = visitor.UpdateFromHr("Vera", "Visit", null, Window(Now, Now.AddDays(3)), Now);

        Assert.Equal("person.not_local", result.Error.Code);
    }

    [Fact]
    public void Extend_beyond_visitor_cap_is_rejected()
    {
        var visitor = Local(PersonType.Visitor, Employee(), 3);

        Assert.Equal("person.sponsorship_too_long", visitor.ChangeEligibilityEnd(Now.AddDays(8), LifecycleSettings.Defaults, Now).Error.Code);
        Assert.True(visitor.ChangeEligibilityEnd(Now.AddDays(7), LifecycleSettings.Defaults, Now).IsSuccess);
        Assert.Equal(Now.AddDays(7), visitor.Eligibility.End);
        Assert.Equal("person.not_local", Employee().ChangeEligibilityEnd(Now.AddDays(7), LifecycleSettings.Defaults, Now).Error.Code);
    }

    [Fact]
    public void End_sponsorship_sets_end_and_raises_events()
    {
        var sponsor = Employee();
        var external = Local(PersonType.External, sponsor, 100);
        external.ClearDomainEvents();
        var at = Now.AddDays(5);

        Assert.True(external.EndSponsorship(sponsor.Id, at).IsSuccess);

        Assert.Equal(at, external.Eligibility.End);
        var ended = Assert.Single(external.DomainEvents.OfType<SponsorshipEnded>());
        Assert.Equal(sponsor.Id, ended.SponsorId);
        Assert.Equal(sponsor.Id, ended.EndedById);
        Assert.Equal(Now.AddDays(100), Assert.Single(external.DomainEvents.OfType<EligibilityChanged>()).PreviousEnd);
    }

    [Fact]
    public void Terminate_twice_is_rejected()
    {
        var person = Employee();

        Assert.True(person.Terminate(Now).IsSuccess);
        Assert.Equal(PersonStatus.Terminated, person.Status);
        Assert.Single(person.DomainEvents.OfType<PersonTerminated>());
        Assert.Equal("person.terminated", person.Terminate(Now).Error.Code);
    }
}
