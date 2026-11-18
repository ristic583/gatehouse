namespace Gatehouse.Infrastructure.Seeding;

public sealed record SeedEntitlement(string Subject, string AreaCode, SeedEntitlementKind Kind, int StartDays, int EndDays);
