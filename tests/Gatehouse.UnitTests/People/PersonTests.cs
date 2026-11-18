using Gatehouse.Domain.People;
using Gatehouse.Domain.People.Events;

namespace Gatehouse.UnitTests.People;

public sealed class PersonTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);

    private static Eligibility Open => Eligibility.Create(Now.AddYears(-1), null, "Europe/Copenhagen").Value;

    private static Eligibility Bounded => Eligibility.Create(Now, Now.AddDays(90), "Europe/Copenhagen").Value;

    private static Person Employee(string email = "boss@nordlys.test") =>
        Person.Create(PersonSource.Authoritative, PersonType.Employee, "Erik", "Moller", email, Open, null, null, Now).Value;

    [Fact]
    public void External_must_be_local()
    {
        var result = Person.Create(PersonSource.Authoritative, PersonType.External, "O", "K", "o@x.test", Bounded, null, Employee(), Now);
        Assert.Equal("person.non_employee_must_be_local", result.Error.Code);
    }

    [Fact]
    public void Local_person_without_sponsor_is_rejected()
    {
        var result = Person.Create(PersonSource.Local, PersonType.External, "O", "K", "o@x.test", Bounded, null, null, Now);
        Assert.Equal("person.local_requires_sponsor", result.Error.Code);
    }

    [Fact]
    public void Local_person_without_end_date_is_rejected()
    {
        var result = Person.Create(PersonSource.Local, PersonType.External, "O", "K", "o@x.test", Open, null, Employee(), Now);
        Assert.Equal("person.local_requires_end_date", result.Error.Code);
    }

    [Fact]
    public void Sponsor_must_be_employee()
    {
        var external = Person.Create(PersonSource.Local, PersonType.External, "O", "K", "o@x.test", Bounded, null, Employee(), Now).Value;
        var result = Person.Create(PersonSource.Local, PersonType.Visitor, "V", "S", "v@x.test", Bounded, null, external, Now);
        Assert.Equal("person.sponsor_not_employee", result.Error.Code);
    }

    [Fact]
    public void Authoritative_employee_may_have_open_end()
    {
        var person = Employee("Anna@Nordlys.TEST");
        Assert.Null(person.Eligibility.End);
        Assert.Equal("anna@nordlys.test", person.Email);
    }

    [Fact]
    public void Manager_cannot_be_self()
    {
        var person = Employee();
        Assert.Equal("person.self_reference", person.SetManager(person.Id).Error.Code);
    }

    [Fact]
    public void Create_raises_person_created()
    {
        var manager = PersonId.New();
        var person = Person.Create(PersonSource.Authoritative, PersonType.Employee, "A", "L", "a@x.test", Open, manager, null, Now).Value;

        var evt = Assert.IsType<PersonCreated>(Assert.Single(person.DomainEvents));
        Assert.Equal(person.Id, evt.PersonId);
        Assert.Equal(manager, evt.ManagerId);
        Assert.Equal(Now, evt.OccurredAt);
    }

    [Fact]
    public void Link_identity_twice_with_same_subject_is_idempotent()
    {
        var person = Employee();
        Assert.True(person.LinkIdentity("sub-1").IsSuccess);
        Assert.True(person.LinkIdentity("sub-1").IsSuccess);
        Assert.Equal("person.identity_already_linked", person.LinkIdentity("sub-2").Error.Code);
    }
}
