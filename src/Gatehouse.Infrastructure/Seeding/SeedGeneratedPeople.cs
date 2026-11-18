namespace Gatehouse.Infrastructure.Seeding;

public sealed record SeedGeneratedPeople(
    IReadOnlyList<string> GivenNames,
    IReadOnlyList<string> FamilyNames,
    string EvenManager,
    string OddManager,
    string ExternalSponsor,
    string VisitorSponsor,
    string GuestSponsor);
