using Gatehouse.Domain.Fulfillment;

namespace Gatehouse.Application.Fulfillment.PacsAdmin;

public sealed record SavePacsInstanceRequest(string Name, PacsVendor Vendor, IReadOnlyList<Guid> SiteIds);
