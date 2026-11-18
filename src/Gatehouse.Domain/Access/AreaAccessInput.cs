using Gatehouse.Domain.Common;

namespace Gatehouse.Domain.Access;

public sealed record AreaAccessInput(
    DateTimeOffset Now,
    DateRange Eligibility,
    bool PersonActive,
    bool AreaMapped,
    DateTimeOffset? OfflineSince,
    bool AnyConnectorOffline,
    IReadOnlyList<EntitlementSnapshot> Entitlements,
    bool? HasActiveCard);

public sealed record EntitlementSnapshot(
    EntitlementStatus Status,
    DateTimeOffset RequestedStart,
    DateTimeOffset EffectiveEnd,
    bool AllGrantsConfirmed,
    bool RevokePendingConfirmation);
