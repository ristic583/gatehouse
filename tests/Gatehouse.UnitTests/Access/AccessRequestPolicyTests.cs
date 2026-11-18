using Gatehouse.Domain.Access;
using Gatehouse.Domain.People;

namespace Gatehouse.UnitTests.Access;

public sealed class AccessRequestPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);

    private static AccessRequestInput Valid(
        PersonStatus status = PersonStatus.Active,
        Eligibility? eligibility = null,
        bool mapped = true,
        IReadOnlyList<(EntitlementStatus, DateTimeOffset)>? existing = null,
        DateTimeOffset? start = null,
        DateTimeOffset? end = null,
        string justification = "Working on the lab project",
        int maxDays = 30) =>
        new(
            status,
            eligibility ?? Eligibility.Create(Now.AddYears(-1), null, "Europe/Copenhagen").Value,
            maxDays,
            mapped,
            existing ?? [],
            start ?? Now.AddDays(1),
            end ?? Now.AddDays(10),
            justification,
            Now);

    private static string Code(AccessRequestInput input) => AccessRequestPolicy.Evaluate(input).Error.Code;

    [Fact]
    public void Inactive_person_is_rejected() => Assert.Equal("request.person_inactive", Code(Valid(status: PersonStatus.Terminated)));

    [Fact]
    public void Short_justification_is_rejected() => Assert.Equal("request.justification_invalid", Code(Valid(justification: "short")));

    [Fact]
    public void End_before_start_is_rejected() =>
        Assert.Equal("request.invalid_window", Code(Valid(start: Now.AddDays(5), end: Now.AddDays(4))));

    [Fact]
    public void Start_in_past_is_rejected() => Assert.Equal("request.start_in_past", Code(Valid(start: Now.AddMinutes(-6))));

    [Fact]
    public void Start_before_eligibility_is_rejected() =>
        Assert.Equal(
            "request.before_eligibility",
            Code(Valid(eligibility: Eligibility.Create(Now.AddDays(2), Now.AddDays(60), "UTC").Value)));

    [Fact]
    public void Start_after_eligibility_end_is_rejected() =>
        Assert.Equal(
            "request.outside_eligibility",
            Code(Valid(eligibility: Eligibility.Create(Now.AddDays(-2), Now.AddDays(1), "UTC").Value, start: Now.AddDays(1))));

    [Fact]
    public void Exceeding_max_duration_is_rejected() =>
        Assert.Equal("request.exceeds_max_duration", Code(Valid(end: Now.AddDays(40))));

    [Fact]
    public void Unmapped_area_is_rejected() => Assert.Equal("request.area_not_mapped", Code(Valid(mapped: false)));

    [Fact]
    public void Open_duplicate_is_rejected() =>
        Assert.Equal("request.duplicate", Code(Valid(existing: [(EntitlementStatus.PendingApproval, Now.AddDays(3))])));

    [Fact]
    public void End_after_eligibility_is_capped()
    {
        var eligibilityEnd = Now.AddDays(5);
        var decision = AccessRequestPolicy.Evaluate(Valid(eligibility: Eligibility.Create(Now.AddDays(-2), eligibilityEnd, "UTC").Value)).Value;
        Assert.Equal(eligibilityEnd, decision.EffectiveEnd);
        Assert.True(decision.CappedToEligibility);
    }

    [Fact]
    public void Expired_previous_entitlement_is_not_duplicate() =>
        Assert.True(AccessRequestPolicy.Evaluate(Valid(existing: [(EntitlementStatus.Approved, Now.AddDays(-1)), (EntitlementStatus.Rejected, Now.AddDays(9))])).IsSuccess);

    [Fact]
    public void Valid_request_is_accepted()
    {
        var decision = AccessRequestPolicy.Evaluate(Valid()).Value;
        Assert.Equal(Now.AddDays(10), decision.EffectiveEnd);
        Assert.False(decision.CappedToEligibility);
    }
}
