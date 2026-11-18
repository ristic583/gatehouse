namespace Gatehouse.Infrastructure.Seeding;

/// <summary>Declarative demo data for one tenant (see <see cref="NordlysSeed"/>, <see cref="BricksSeed"/>).</summary>
public sealed record SeedDefinition(
    string TimeZoneId,
    string EmailDomain,
    IReadOnlyList<SeedSite> Sites,
    IReadOnlyList<SeedArea> Areas,
    IReadOnlyList<SeedPacs> PacsInstances,
    IReadOnlySet<string> UnmappedAreaCodes,
    IReadOnlyList<SeedPerson> NamedPeople,
    SeedGeneratedPeople Generated,
    IReadOnlyList<SeedEntitlement> Entitlements,
    string? ThemeJson = null);
