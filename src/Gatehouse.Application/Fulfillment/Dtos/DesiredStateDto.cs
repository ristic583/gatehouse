namespace Gatehouse.Application.Fulfillment.Dtos;

public sealed record DesiredStateDto(
    string Action,
    string PersonRef,
    string PersonName,
    string VendorAccessLevelRef,
    string AccessLevelDisplayName,
    DateTimeOffset ValidFrom,
    DateTimeOffset ValidTo,
    string PacsInstanceName,
    string Vendor);
