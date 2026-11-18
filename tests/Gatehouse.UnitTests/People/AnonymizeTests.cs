using Gatehouse.Domain.People;

namespace Gatehouse.UnitTests.People;

public sealed class AnonymizeTests
{
    private static Person Employee() =>
        Person.Create(PersonSource.Authoritative, PersonType.Employee, "Anna", "Larsen", "anna@x.test",
            Eligibility.Create(DateTimeOffset.UtcNow.AddYears(-1), null, "Europe/Copenhagen").Value, null, null, DateTimeOffset.UtcNow).Value;

    [Fact]
    public void Active_person_cannot_be_anonymized()
    {
        Assert.Equal("person.not_terminated", Employee().Anonymize(DateTimeOffset.UtcNow).Error.Code);
    }

    [Fact]
    public void Terminated_person_is_anonymized_once()
    {
        var person = Employee();
        person.LinkIdentity("sub-1");
        var terminatedAt = DateTimeOffset.UtcNow;
        person.Terminate(terminatedAt);
        var first = DateTimeOffset.UtcNow;

        Assert.True(person.Anonymize(first).IsSuccess);
        Assert.True(person.Anonymize(first.AddDays(1)).IsSuccess);

        Assert.Equal(terminatedAt, person.TerminatedAt);
        Assert.Equal(first, person.AnonymizedAt);
        Assert.Equal("Anonymized", person.GivenName);
        Assert.DoesNotContain("anna", person.Email, StringComparison.Ordinal);
        Assert.Null(person.IdentitySubject);
    }
}
