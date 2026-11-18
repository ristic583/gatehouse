namespace Gatehouse.Application.People.Dtos;

public sealed record PersonSummaryDto(
    Guid PersonId,
    string DisplayName,
    string Email,
    string Type,
    string Source,
    string Status,
    DateTimeOffset EligibilityStart,
    DateTimeOffset? EligibilityEnd,
    PersonRefDto? Sponsor,
    PersonRefDto? Manager);
