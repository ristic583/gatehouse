namespace Gatehouse.Application.Credentials.Dtos;

/// <summary><c>PacsStatus</c>: null, Pending, Confirmed or Failed, from the newest ActivateCard/SuspendCard batch of the card.</summary>
public sealed record CardDto(
    Guid CardId, Guid PersonId, string CardNumberMasked, string State, DateTimeOffset StateChangedAt, string? PacsStatus);
