namespace Gatehouse.Application.Fulfillment.Dtos;

public sealed record ConfirmedStateDto(
    string ConfirmedByKind,
    string ConfirmedById,
    string? ConfirmedByName,
    DateTimeOffset ConfirmedAt,
    string? PacsRef);
