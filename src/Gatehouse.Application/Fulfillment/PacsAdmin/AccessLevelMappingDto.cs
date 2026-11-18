namespace Gatehouse.Application.Fulfillment.PacsAdmin;

public sealed record AccessLevelMappingDto(Guid AreaId, string AreaName, string VendorAccessLevelRef, string DisplayName);
