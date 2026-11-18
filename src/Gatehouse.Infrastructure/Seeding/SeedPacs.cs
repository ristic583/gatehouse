using Gatehouse.Domain.Fulfillment;

namespace Gatehouse.Infrastructure.Seeding;

public sealed record SeedPacs(string Name, PacsVendor Vendor, string SiteCode);
