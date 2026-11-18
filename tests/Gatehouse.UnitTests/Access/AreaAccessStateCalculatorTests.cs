using Gatehouse.Domain.Access;
using Gatehouse.Domain.Common;

namespace Gatehouse.UnitTests.Access;

public sealed class AreaAccessStateCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);

    private static EntitlementSnapshot Confirmed(DateTimeOffset? start = null, DateTimeOffset? end = null) =>
        new(EntitlementStatus.Approved, start ?? Now.AddDays(-10), end ?? Now.AddDays(10), true, false);

    private static AreaAccessResult Calc(
        IReadOnlyList<EntitlementSnapshot> entitlements,
        bool mapped = true,
        bool offline = false,
        DateRange? eligibility = null,
        bool? card = null) =>
        AreaAccessStateCalculator.Calculate(new AreaAccessInput(
            Now,
            eligibility ?? new DateRange(Now.AddYears(-1), null),
            true,
            mapped,
            offline ? Now.AddMinutes(-5) : null,
            offline,
            entitlements,
            card));

    [Fact]
    public void Unmapped_area_is_unknown()
    {
        var r = Calc([Confirmed()], mapped: false);
        Assert.Equal((AreaAccessState.Unknown, AreaAccessReason.AreaNotMapped), (r.State, r.Reason));
    }

    [Fact]
    public void Offline_connector_is_unknown_even_with_confirmed_access()
    {
        var r = Calc([Confirmed()], offline: true);
        Assert.Equal((AreaAccessState.Unknown, AreaAccessReason.ConnectorOffline), (r.State, r.Reason));
        Assert.Equal(Now.AddMinutes(-5), r.Since);
    }

    [Fact]
    public void Confirmed_within_window_and_eligibility_is_has_access()
    {
        var r = Calc([Confirmed()]);
        Assert.Equal((AreaAccessState.HasAccess, AreaAccessReason.Confirmed), (r.State, r.Reason));
        Assert.False(r.CardInactive);
    }

    [Fact]
    public void Approved_not_confirmed_is_pending_never_green()
    {
        var r = Calc([new EntitlementSnapshot(EntitlementStatus.Approved, Now.AddDays(-1), Now.AddDays(10), false, false)]);
        Assert.Equal((AreaAccessState.Pending, AreaAccessReason.AwaitingFulfillment), (r.State, r.Reason));
    }

    [Fact]
    public void Confirmed_but_outside_eligibility_is_no_access()
    {
        var r = Calc([Confirmed()], eligibility: new DateRange(Now.AddYears(-1), Now.AddDays(-1)));
        Assert.Equal((AreaAccessState.NoAccess, AreaAccessReason.OutsideWindow), (r.State, r.Reason));
    }

    [Fact]
    public void Confirmed_before_start_is_pending_scheduled()
    {
        var r = Calc([Confirmed(start: Now.AddDays(1))]);
        Assert.Equal((AreaAccessState.Pending, AreaAccessReason.ScheduledStart), (r.State, r.Reason));
    }

    [Fact]
    public void Pending_approval_is_pending()
    {
        var r = Calc([new EntitlementSnapshot(EntitlementStatus.PendingApproval, Now.AddDays(1), Now.AddDays(10), false, false)]);
        Assert.Equal((AreaAccessState.Pending, AreaAccessReason.AwaitingApproval), (r.State, r.Reason));
    }

    [Fact]
    public void Old_pending_request_is_still_pending()
    {
        var r = Calc([new EntitlementSnapshot(EntitlementStatus.PendingApproval, Now.AddDays(-400), Now.AddDays(-300), false, false)]);
        Assert.Equal((AreaAccessState.Pending, AreaAccessReason.AwaitingApproval), (r.State, r.Reason));
    }

    [Fact]
    public void Unconfirmed_revocation_is_pending()
    {
        var r = Calc([new EntitlementSnapshot(EntitlementStatus.Revoked, Now.AddDays(-10), Now.AddDays(10), true, true)]);
        Assert.Equal((AreaAccessState.Pending, AreaAccessReason.RevocationPending), (r.State, r.Reason));
    }

    [Fact]
    public void No_entitlement_is_no_access()
    {
        var r = Calc([new EntitlementSnapshot(EntitlementStatus.Rejected, Now, Now.AddDays(1), false, false)]);
        Assert.Equal((AreaAccessState.NoAccess, AreaAccessReason.NoEntitlement), (r.State, r.Reason));
    }

    [Fact]
    public void Inactive_card_keeps_green_and_sets_note()
    {
        var r = Calc([Confirmed()], card: false);
        Assert.Equal(AreaAccessState.HasAccess, r.State);
        Assert.True(r.CardInactive);
    }
}
