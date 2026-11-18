namespace Gatehouse.Infrastructure.Seeding;

public sealed record SeedSite(string Name, string Code, IReadOnlyList<SeedFloor> Floors);
