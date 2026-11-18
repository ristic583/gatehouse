using Gatehouse.Domain.People;

namespace Gatehouse.Infrastructure.Seeding;

public sealed record SeedPerson(
    string Username,
    string GivenName,
    string FamilyName,
    PersonType Type = PersonType.Employee,
    string? Manager = null,
    string? Sponsor = null,
    AssignedRole? Role = null);
