using Gatehouse.Domain.People;

namespace Gatehouse.Domain.Access;

public sealed record AccessRequestInput(
    PersonStatus SubjectStatus,
    Eligibility Eligibility,
    int MaxDurationDays,
    bool AreaMapped,
    IReadOnlyList<(EntitlementStatus Status, DateTimeOffset EffectiveEnd)> ExistingForArea,
    DateTimeOffset RequestedStart,
    DateTimeOffset RequestedEnd,
    string Justification,
    DateTimeOffset Now);
