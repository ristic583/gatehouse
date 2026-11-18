namespace Gatehouse.Infrastructure.Seeding;

public sealed record SeedArea(
    string Code, string Name, string SiteCode, int Level, string SecurityLevel, double X1, double Y1, double X2, double Y2, string Owner);
