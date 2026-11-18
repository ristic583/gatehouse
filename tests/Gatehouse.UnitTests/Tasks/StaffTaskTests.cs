using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;
using Gatehouse.Domain.Tasks;
using Gatehouse.Domain.Tasks.Events;

namespace Gatehouse.UnitTests.Tasks;

public sealed class StaffTaskTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);

    private static StaffTask New() =>
        StaffTask.Open(StaffTaskKind.FulfillmentFailed, SiteId.New(), "Fulfillment", Guid.NewGuid(), Now);

    [Fact]
    public void Open_task_is_open_and_raises_created()
    {
        var t = New();
        Assert.Equal(StaffTaskStatus.Open, t.Status);
        Assert.Null(t.AssignedToId);
        var ev = Assert.Single(t.DomainEvents.OfType<StaffTaskCreated>());
        Assert.Equal(t.Id, ev.TaskId);
        Assert.Equal(StaffTaskKind.FulfillmentFailed, ev.Kind);
    }

    [Fact]
    public void Claim_sets_in_progress_and_assignee()
    {
        var t = New();
        var p = PersonId.New();
        Assert.True(t.Claim(p, Now.AddMinutes(1)).IsSuccess);
        Assert.Equal(StaffTaskStatus.InProgress, t.Status);
        Assert.Equal(p, t.AssignedToId);
        Assert.Equal(Now.AddMinutes(1), t.UpdatedAt);
    }

    [Fact]
    public void Claim_by_another_person_takes_over()
    {
        var t = New();
        var other = PersonId.New();
        t.Claim(PersonId.New(), Now);
        Assert.True(t.Claim(other, Now).IsSuccess);
        Assert.Equal(other, t.AssignedToId);
    }

    [Fact]
    public void Resolve_requires_action()
    {
        var t = New();
        var r = t.Resolve(PersonId.New(), null, null, Now);
        Assert.Equal("task.resolution_required", r.Error.Code);
        Assert.Equal(StaffTaskStatus.Open, t.Status);
        Assert.Equal("task.resolution_required", t.Resolve(PersonId.New(), StaffTaskResolutionAction.Dismissed, new string('x', 501), Now).Error.Code);
    }

    [Fact]
    public void Resolve_records_actor_action_and_time()
    {
        var t = New();
        var p = PersonId.New();
        Assert.True(t.Resolve(p, StaffTaskResolutionAction.Retried, "ok", Now.AddMinutes(2)).IsSuccess);
        Assert.Equal(StaffTaskStatus.Resolved, t.Status);
        Assert.Equal(p, t.ResolvedById);
        Assert.Equal(StaffTaskResolutionAction.Retried, t.ResolutionAction);
        Assert.Equal("ok", t.ResolutionNote);
        Assert.Equal(Now.AddMinutes(2), t.ResolvedAt);
        var ev = Assert.Single(t.DomainEvents.OfType<StaffTaskResolved>());
        Assert.Equal("Retried", ev.ResolutionAction);
    }

    [Fact]
    public void Resolved_task_cannot_be_claimed_or_resolved_again()
    {
        var t = New();
        t.Resolve(null, StaffTaskResolutionAction.AutoResolved, null, Now);
        Assert.Equal("task.invalid_transition", t.Claim(PersonId.New(), Now).Error.Code);
        Assert.Equal("task.invalid_transition", t.Resolve(null, StaffTaskResolutionAction.Dismissed, null, Now).Error.Code);
    }

    [Fact]
    public void System_resolution_has_no_person()
    {
        var t = New();
        t.Resolve(null, StaffTaskResolutionAction.AutoResolved, null, Now);
        Assert.Null(t.ResolvedById);
        Assert.Null(Assert.Single(t.DomainEvents.OfType<StaffTaskResolved>()).ResolvedById);
    }
}
