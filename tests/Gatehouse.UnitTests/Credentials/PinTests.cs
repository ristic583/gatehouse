using Gatehouse.Domain.Credentials;
using Gatehouse.Domain.Credentials.Events;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Tasks;

namespace Gatehouse.UnitTests.Credentials;

public sealed class PinTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Pin_to_string_is_masked()
    {
        var pin = Pin.Create("4821").Value;

        Assert.Equal("****", pin.ToString());
        Assert.Equal("****", $"{pin}");
        Assert.Equal("4821", pin.Reveal());
    }

    [Theory]
    [InlineData("12a4")]
    [InlineData("123")]
    [InlineData("123456789")]
    [InlineData("")]
    [InlineData(null)]
    public void Pin_rejects_letters_and_short_values(string? digits) => Assert.Equal("pin.invalid", Pin.Create(digits).Error.Code);

    [Fact]
    public void Pin_credential_raises_set_and_reveal_events_without_value()
    {
        var person = PersonId.New();
        var credential = PinCredential.Set(person, [1, 2, 3], "local-v1", person, Now);
        credential.RecordReveal(PersonId.New(), StaffTaskId.New(), Now);

        Assert.Single(credential.DomainEvents.OfType<PinSet>());
        Assert.Single(credential.DomainEvents.OfType<PinRevealed>());
        Assert.Equal(person, credential.PersonId);
    }
}
