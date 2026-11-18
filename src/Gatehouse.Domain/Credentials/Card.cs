using System.Text.RegularExpressions;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Credentials.Events;
using Gatehouse.Domain.People;

namespace Gatehouse.Domain.Credentials;

public sealed partial class Card : AggregateRoot<CredentialId>
{
    private Card(CredentialId id, PersonId personId, string cardNumber, CardState state, DateTimeOffset now)
        : base(id)
    {
        PersonId = personId;
        CardNumber = cardNumber;
        State = state;
        StateChangedAt = now;
    }

    // EF Core
    private Card()
        : base(default)
    {
        CardNumber = string.Empty;
    }

    public PersonId PersonId { get; private set; }

    public string CardNumber { get; private set; }

    public CardState State { get; private set; }

    public DateTimeOffset StateChangedAt { get; private set; }

    public uint Version { get; private set; }

    public const int ReasonMaxLength = 200;

    public static Result<Card> Create(PersonId personId, string cardNumber, DateTimeOffset now)
    {
        var card = Build(personId, cardNumber, CardState.Assigned, now);
        if (card.IsSuccess)
        {
            card.Value.Raise(new CardAssigned(card.Value.Id, personId, CardNumberMask.Mask(cardNumber), now));
        }

        return card;
    }

    /// <summary>Seed only: an already active card, no events.</summary>
    public static Result<Card> CreateActive(PersonId personId, string cardNumber, DateTimeOffset now) =>
        Build(personId, cardNumber, CardState.Active, now);

    public Result RequestActivation(PersonId requestedBy, DateTimeOffset now) =>
        Move(
            State is CardState.Assigned or CardState.Deactivated, CardState.ActivationPending, now,
            new CardActivationRequested(Id, PersonId, requestedBy, now));

    public Result ConfirmActivation(DateTimeOffset now) =>
        Move(State == CardState.ActivationPending, CardState.Active, now, new CardActivated(Id, PersonId, now));

    public Result FailActivation(DateTimeOffset now) =>
        Move(State == CardState.ActivationPending, CardState.Assigned, now, null);

    public Result Deactivate(string reason, DateTimeOffset now) =>
        IsValidReason(reason)
            ? Move(State == CardState.Active, CardState.Deactivated, now, new CardDeactivated(Id, PersonId, reason.Trim(), now))
            : ReasonRequired();

    public Result Revoke(string reason, DateTimeOffset now) =>
        IsValidReason(reason)
            ? Move(State != CardState.Revoked, CardState.Revoked, now, new CardRevoked(Id, PersonId, reason.Trim(), now))
            : ReasonRequired();

    private static bool IsValidReason(string? reason) => !string.IsNullOrWhiteSpace(reason) && reason.Trim().Length <= ReasonMaxLength;

    private static Error ReasonRequired() =>
        Error.Validation("card.reason_required", $"A reason of 1-{ReasonMaxLength} characters is required.");

    [GeneratedRegex("^[0-9]{6,20}$")]
    private static partial Regex NumberPattern();

    private static Result<Card> Build(PersonId personId, string cardNumber, CardState state, DateTimeOffset now)
    {
        if (cardNumber is null || !NumberPattern().IsMatch(cardNumber))
        {
            return Error.Validation("card.invalid_number", "Card number must be 6-20 digits.");
        }

        return new Card(CredentialId.New(), personId, cardNumber, state, now);
    }

    private Result Move(bool allowed, CardState target, DateTimeOffset now, DomainEvent? @event)
    {
        if (!allowed)
        {
            return Error.Conflict("card.invalid_transition", $"Card cannot move from {State} to {target}.");
        }

        State = target;
        StateChangedAt = now;
        if (@event is not null)
        {
            Raise(@event);
        }

        return Result.Success();
    }
}
