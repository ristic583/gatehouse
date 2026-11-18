namespace Gatehouse.Application.People.Dtos;

public sealed record OnboardPersonDto(
    string Type,
    string GivenName,
    string FamilyName,
    string Email,
    DateTimeOffset Start,
    DateTimeOffset End,
    string TimeZoneId,
    Guid? SponsorId,
    string? CardNumber);
