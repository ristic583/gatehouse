using Gatehouse.Application.People.Dtos;

namespace Gatehouse.Application.Access.Dtos;

public sealed record AreaRosterEntryDto(
    Guid EntitlementId,
    PersonRefDto Subject,
    string PersonType,
    DateTimeOffset RequestedStart,
    DateTimeOffset EffectiveEnd,
    string Status,
    string? FulfillmentStatus,
    string? RevocationFulfillmentStatus,
    DateTimeOffset? RevokedAt);
