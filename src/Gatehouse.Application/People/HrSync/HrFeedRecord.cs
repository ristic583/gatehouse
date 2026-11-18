namespace Gatehouse.Application.People.HrSync;

public sealed record HrFeedRecord(
    string Email,
    string GivenName,
    string FamilyName,
    string? ManagerEmail,
    DateTimeOffset EmploymentStart,
    DateTimeOffset? EmploymentEnd,
    string TimeZoneId);
