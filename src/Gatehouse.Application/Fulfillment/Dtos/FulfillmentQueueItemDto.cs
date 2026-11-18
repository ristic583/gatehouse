using Gatehouse.Application.Access.Dtos;
using Gatehouse.Application.People.Dtos;

namespace Gatehouse.Application.Fulfillment.Dtos;

public sealed record FulfillmentQueueItemDto(
    Guid FulfillmentId,
    Guid EntitlementId,
    string Kind,
    string Status,
    string Priority,
    int Revision,
    PersonRefDto Subject,
    AreaRefDto Area,
    string PacsInstanceName,
    string VendorAccessLevelRef,
    string EntitlementStatus,
    string? LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
