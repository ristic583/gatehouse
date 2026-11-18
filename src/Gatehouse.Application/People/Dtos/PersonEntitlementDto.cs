namespace Gatehouse.Application.People.Dtos;

public sealed record PersonEntitlementDto(
    Guid EntitlementId, string AreaName, string Status, DateTimeOffset EffectiveEnd, IReadOnlyList<string> RevokeFulfillmentStatuses,
    string? RevokeAction = null);
