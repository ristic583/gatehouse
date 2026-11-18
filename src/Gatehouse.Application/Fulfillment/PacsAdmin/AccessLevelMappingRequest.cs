namespace Gatehouse.Application.Fulfillment.PacsAdmin;

public sealed record AccessLevelMappingRequest(Guid AreaId, string VendorAccessLevelRef, string? DisplayName);
