using Gatehouse.Application.LiveBuilding.Dtos;
using Gatehouse.Domain.Fulfillment;

namespace Gatehouse.Application.Fulfillment.PacsAdmin;

public sealed record PacsInstanceAdminDto(
    Guid Id,
    string Name,
    PacsVendor Vendor,
    IReadOnlyList<Guid> SiteIds,
    FulfillmentMode FulfillmentMode,
    IReadOnlyList<AccessLevelMappingDto> Mappings,
    int UnmappedAreas,
    AdminConnectorDto? Connector,
    ConnectorHealthDto? Health);
