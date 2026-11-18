namespace Gatehouse.Application.People.Dtos;

public sealed record CreateEmployeeRequest(string Email, string GivenName, string FamilyName, Guid? ManagerId, string? TimeZoneId);
