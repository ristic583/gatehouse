using Gatehouse.Domain.Credentials;
using Gatehouse.Domain.People;

namespace Gatehouse.UnitTests.Credentials;

public sealed class CardTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);

    private static Card In(CardState state)
    {
        var c = Card.Create(PersonId.New(), "100200300", Now).Value;
        switch (state)
        {
            case CardState.ActivationPending:
                c.RequestActivation(PersonId.New(), Now);
                break;
            case CardState.Active:
                c.RequestActivation(PersonId.New(), Now);
                c.ConfirmActivation(Now);
                break;
            case CardState.Deactivated:
                c.RequestActivation(PersonId.New(), Now);
                c.ConfirmActivation(Now);
                c.Deactivate("Lost", Now);
                break;
            case CardState.Revoked:
                c.Revoke("Lost", Now);
                break;
        }

        Assert.Equal(state, c.State);
        return c;
    }

    [Theory]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("123456789012345678901")]
    [InlineData("12345a")]
    public void Create_rejects_invalid_number(string number)
    {
        var r = Card.Create(PersonId.New(), number, Now);
        Assert.True(r.IsFailure);
        Assert.Equal("card.invalid_number", r.Error.Code);
    }

    [Theory]
    [InlineData(CardState.Assigned, "RequestActivation", CardState.ActivationPending)]
    [InlineData(CardState.ActivationPending, "ConfirmActivation", CardState.Active)]
    [InlineData(CardState.ActivationPending, "FailActivation", CardState.Assigned)]
    [InlineData(CardState.Active, "Deactivate", CardState.Deactivated)]
    [InlineData(CardState.Deactivated, "RequestActivation", CardState.ActivationPending)]
    [InlineData(CardState.Assigned, "Revoke", CardState.Revoked)]
    [InlineData(CardState.ActivationPending, "Revoke", CardState.Revoked)]
    [InlineData(CardState.Active, "Revoke", CardState.Revoked)]
    [InlineData(CardState.Deactivated, "Revoke", CardState.Revoked)]
    public void Allowed_transitions_follow_state_machine(CardState from, string action, CardState to)
    {
        var c = In(from);
        Assert.True(Apply(c, action).IsSuccess);
        Assert.Equal(to, c.State);
    }

    [Theory]
    [InlineData("RequestActivation")]
    [InlineData("ConfirmActivation")]
    [InlineData("FailActivation")]
    [InlineData("Deactivate")]
    [InlineData("Revoke")]
    public void Revoked_is_terminal(string action)
    {
        var c = In(CardState.Revoked);
        var r = Apply(c, action);
        Assert.Equal("card.invalid_transition", r.Error.Code);
    }

    private static Gatehouse.Domain.Common.Result Apply(Card c, string action) => action switch
    {
        "RequestActivation" => c.RequestActivation(PersonId.New(), Now),
        "ConfirmActivation" => c.ConfirmActivation(Now),
        "FailActivation" => c.FailActivation(Now),
        "Deactivate" => c.Deactivate("Lost", Now),
        _ => c.Revoke("Lost", Now),
    };

    [Fact]
    public void Activation_request_raises_event_with_requester()
    {
        var sponsor = PersonId.New();
        var c = Card.Create(PersonId.New(), "123456789", Now).Value;
        var masked = Assert.Single(c.DomainEvents.OfType<Gatehouse.Domain.Credentials.Events.CardAssigned>()).CardNumberMasked;
        Assert.EndsWith("6789", masked, StringComparison.Ordinal);
        Assert.DoesNotContain("12345", masked, StringComparison.Ordinal);

        Assert.True(c.RequestActivation(sponsor, Now).IsSuccess);

        Assert.Equal(sponsor, Assert.Single(c.DomainEvents.OfType<Gatehouse.Domain.Credentials.Events.CardActivationRequested>()).RequestedById);
        Assert.True(c.ConfirmActivation(Now).IsSuccess);
        Assert.Single(c.DomainEvents.OfType<Gatehouse.Domain.Credentials.Events.CardActivated>());
    }

    [Fact]
    public void Deactivate_requires_reason()
    {
        var c = Card.CreateActive(PersonId.New(), "123456789", Now).Value;

        Assert.Equal("card.reason_required", c.Deactivate(" ", Now).Error.Code);
        Assert.Equal(CardState.Active, c.State);
        Assert.True(c.Deactivate("Lost card", Now).IsSuccess);
        Assert.Equal("Lost card", Assert.Single(c.DomainEvents.OfType<Gatehouse.Domain.Credentials.Events.CardDeactivated>()).Reason);
    }
}
