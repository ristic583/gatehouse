using Gatehouse.Domain.Access;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.Fulfillment.Events;
using Gatehouse.Domain.People;
using FulfillmentAggregate = Gatehouse.Domain.Fulfillment.Fulfillment;

namespace Gatehouse.UnitTests.Fulfillment;

public sealed class FulfillmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);
    private static readonly PersonId Staff = PersonId.New();

    private static FulfillmentAggregate New() =>
        FulfillmentAggregate.Create(EntitlementId.New(), PacsInstanceId.New(), FulfillmentKind.Grant, CommandPriority.Normal, Now);

    private static FulfillmentAggregate In(FulfillmentStatus status)
    {
        var f = New();
        switch (status)
        {
            case FulfillmentStatus.Desired:
                break;
            case FulfillmentStatus.Pending:
                f.MarkPending(null, Now);
                break;
            case FulfillmentStatus.Confirmed:
                f.ConfirmByStaff(Staff, Now);
                break;
            case FulfillmentStatus.Failed:
                f.Fail("x", "y", Now);
                break;
            case FulfillmentStatus.Drift:
                f.ConfirmByStaff(Staff, Now);
                f.DetectDrift(null, Now);
                break;
        }

        Assert.Equal(status, f.Status);
        return f;
    }

    private static Result Apply(FulfillmentAggregate f, string action) => action switch
    {
        "MarkPending" => f.MarkPending(CommandId.New(), Now),
        "ConfirmByAgent" => f.ConfirmByAgent("conn-1", "ref", Now),
        "ConfirmByStaff" => f.ConfirmByStaff(Staff, Now),
        "Fail" => f.Fail("pacs.error", "boom", Now),
        "Retry" => f.Retry(Now),
        "DetectDrift" => f.DetectDrift(null, Now),
        "Remediate" => f.Remediate(Now),
        "ResolveAsInSync" => f.ResolveAsInSync(Staff, Now),
        _ => throw new ArgumentOutOfRangeException(nameof(action)),
    };

    [Fact]
    public void Create_is_desired_with_revision_one()
    {
        var f = New();
        Assert.Equal(FulfillmentStatus.Desired, f.Status);
        Assert.Equal(1, f.Revision);
        Assert.IsType<FulfillmentDesired>(Assert.Single(f.DomainEvents));
    }

    [Fact]
    public void Deferred_regrant_is_its_own_fulfillment()
    {
        var revoke = FulfillmentAggregate.Create(EntitlementId.New(), PacsInstanceId.New(), FulfillmentKind.Revoke, CommandPriority.High, Now);
        revoke.MarkPending(CommandId.New(), Now);
        var remainder = EntitlementId.New();

        var regrant = revoke.PlanDeferredRegrant(remainder, Now);

        Assert.True(regrant.IsSuccess);
        Assert.NotEqual(revoke.Id, regrant.Value.Id);
        Assert.Equal(FulfillmentKind.Grant, regrant.Value.Kind);
        Assert.Equal(remainder, regrant.Value.EntitlementId);
        Assert.Equal(revoke.PacsInstanceId, regrant.Value.PacsInstanceId);
        Assert.Equal(CommandPriority.Normal, regrant.Value.Priority);
        Assert.Equal(FulfillmentStatus.Desired, regrant.Value.Status);
        Assert.IsType<FulfillmentDesired>(Assert.Single(regrant.Value.DomainEvents));
        Assert.Equal(FulfillmentStatus.Pending, revoke.Status);
        Assert.True(New().PlanDeferredRegrant(remainder, Now).IsFailure);
    }

    [Theory]
    [InlineData(FulfillmentStatus.Desired, "MarkPending", FulfillmentStatus.Pending)]
    [InlineData(FulfillmentStatus.Pending, "ConfirmByAgent", FulfillmentStatus.Confirmed)]
    [InlineData(FulfillmentStatus.Pending, "ConfirmByStaff", FulfillmentStatus.Confirmed)]
    [InlineData(FulfillmentStatus.Pending, "Fail", FulfillmentStatus.Failed)]
    [InlineData(FulfillmentStatus.Desired, "ConfirmByStaff", FulfillmentStatus.Confirmed)]
    [InlineData(FulfillmentStatus.Desired, "Fail", FulfillmentStatus.Failed)]
    [InlineData(FulfillmentStatus.Failed, "Retry", FulfillmentStatus.Desired)]
    [InlineData(FulfillmentStatus.Confirmed, "DetectDrift", FulfillmentStatus.Drift)]
    [InlineData(FulfillmentStatus.Drift, "Remediate", FulfillmentStatus.Desired)]
    [InlineData(FulfillmentStatus.Drift, "ResolveAsInSync", FulfillmentStatus.Confirmed)]
    public void Allowed_transitions_succeed(FulfillmentStatus from, string action, FulfillmentStatus to)
    {
        var f = In(from);
        Assert.True(Apply(f, action).IsSuccess);
        Assert.Equal(to, f.Status);
    }

    [Theory]
    [InlineData(FulfillmentStatus.Desired, "DetectDrift")]
    [InlineData(FulfillmentStatus.Desired, "ConfirmByAgent")]
    [InlineData(FulfillmentStatus.Confirmed, "MarkPending")]
    [InlineData(FulfillmentStatus.Failed, "ConfirmByStaff")]
    [InlineData(FulfillmentStatus.Pending, "Retry")]
    [InlineData(FulfillmentStatus.Confirmed, "Fail")]
    [InlineData(FulfillmentStatus.Pending, "ResolveAsInSync")]
    public void Disallowed_transitions_fail(FulfillmentStatus from, string action)
    {
        var f = In(from);
        Assert.Equal("fulfillment.invalid_transition", Apply(f, action).Error.Code);
        Assert.Equal(from, f.Status);
    }

    [Fact]
    public void Retry_increments_revision()
    {
        var f = In(FulfillmentStatus.Failed);
        f.Retry(Now);
        Assert.Equal(2, f.Revision);
    }

    [Theory]
    [InlineData(FulfillmentStatus.Confirmed)]
    [InlineData(FulfillmentStatus.Pending)]
    public void Reapply_reopens_with_new_revision_and_raises_desired(FulfillmentStatus from)
    {
        var f = In(from);
        f.ClearDomainEvents();

        Assert.True(f.Reapply(CommandPriority.High, Now).IsSuccess);

        Assert.Equal((FulfillmentStatus.Desired, 2, CommandPriority.High), (f.Status, f.Revision, f.Priority));
        Assert.Null(f.CommandId);
        Assert.IsType<FulfillmentDesired>(Assert.Single(f.DomainEvents));
    }

    [Theory]
    [InlineData(FulfillmentStatus.Desired)]
    [InlineData(FulfillmentStatus.Failed)]
    [InlineData(FulfillmentStatus.Drift)]
    public void Reapply_from_other_states_fails(FulfillmentStatus from) =>
        Assert.True(In(from).Reapply(CommandPriority.Normal, Now).IsFailure);

    [Fact]
    public void Reapply_never_lowers_priority()
    {
        var f = FulfillmentAggregate.Create(EntitlementId.New(), PacsInstanceId.New(), FulfillmentKind.Revoke, CommandPriority.High, Now);
        f.ConfirmByStaff(Staff, Now);

        f.Reapply(CommandPriority.Normal, Now);

        Assert.Equal(CommandPriority.High, f.Priority);
    }

    [Fact]
    public void Staff_confirmation_records_actor_and_time()
    {
        var f = In(FulfillmentStatus.Pending);
        f.ConfirmByStaff(Staff, Now.AddMinutes(3));
        Assert.Equal(ConfirmerKind.Staff, f.ConfirmedByKind);
        Assert.Equal(Staff.Value.ToString(), f.ConfirmedById);
        Assert.Equal(Now.AddMinutes(3), f.ConfirmedAt);
        Assert.Single(f.DomainEvents.OfType<FulfillmentConfirmed>());
    }

    [Theory]
    [InlineData(FulfillmentStatus.Desired)]
    [InlineData(FulfillmentStatus.Pending)]
    [InlineData(FulfillmentStatus.Failed)]
    public void Supersede_is_allowed_from_desired_pending_and_failed(FulfillmentStatus from)
    {
        var f = In(from);
        Assert.True(f.Supersede(Now.AddMinutes(1)).IsSuccess);
        Assert.Equal(FulfillmentStatus.Superseded, f.Status);
        Assert.Single(f.DomainEvents.OfType<FulfillmentSuperseded>());
    }

    [Fact]
    public void Confirmed_fulfillment_cannot_be_superseded()
    {
        var f = In(FulfillmentStatus.Confirmed);
        Assert.Equal("fulfillment.invalid_transition", f.Supersede(Now).Error.Code);
    }

    [Fact]
    public void Superseded_fulfillment_cannot_be_confirmed()
    {
        var f = New();
        f.Supersede(Now);
        Assert.True(f.ConfirmByStaff(Staff, Now).IsFailure);
        Assert.True(f.ConfirmByAgent("c", null, Now).IsFailure);
    }

    [Fact]
    public void Revocation_alert_raises_event_with_outstanding_time()
    {
        var f = FulfillmentAggregate.Create(EntitlementId.New(), PacsInstanceId.New(), FulfillmentKind.Revoke, CommandPriority.Normal, Now);
        Assert.True(f.FlagRevocationUnconfirmed(Now, Now.AddMinutes(10)).IsSuccess);
        var ev = Assert.Single(f.DomainEvents.OfType<RevocationUnconfirmedAlert>());
        Assert.Equal(TimeSpan.FromMinutes(10), ev.Outstanding);
        Assert.Equal(FulfillmentStatus.Desired, f.Status);
    }

    [Fact]
    public void Revocation_alert_on_grant_fails()
    {
        Assert.True(New().FlagRevocationUnconfirmed(Now, Now).IsFailure);
    }

    [Fact]
    public void Mark_pending_with_command_raises_dispatched()
    {
        var f = New();
        var id = CommandId.New();
        f.MarkPending(id, Now);
        var e = Assert.IsType<FulfillmentDispatched>(f.DomainEvents[^1]);
        Assert.Equal(id, e.CommandId);
        Assert.Equal(1, e.Attempt);
    }

    [Fact]
    public void Mark_pending_without_command_raises_nothing()
    {
        var f = New();
        f.ClearDomainEvents();
        f.MarkPending(null, Now);
        Assert.Empty(f.DomainEvents);
    }

    [Fact]
    public void Detect_drift_carries_finding_id()
    {
        var f = In(FulfillmentStatus.Confirmed);
        f.ClearDomainEvents();
        var findingId = Guid.CreateVersion7();
        Assert.True(f.DetectDrift(findingId, Now).IsSuccess);
        var drift = Assert.IsType<FulfillmentDriftDetected>(Assert.Single(f.DomainEvents));
        Assert.Equal(findingId, drift.FindingId);
        Assert.Equal(FulfillmentStatus.Drift, f.Status);
    }
}
