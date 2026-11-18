using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;
using Gatehouse.Domain.Tasks.Events;

namespace Gatehouse.Domain.Tasks;

public sealed class StaffTask : AggregateRoot<StaffTaskId>
{
    private StaffTask(
        StaffTaskId id,
        StaffTaskKind kind,
        SiteId? siteId,
        string subjectType,
        Guid subjectId,
        DateTimeOffset now)
        : base(id)
    {
        Kind = kind;
        SiteId = siteId;
        SubjectType = subjectType;
        SubjectId = subjectId;
        Status = StaffTaskStatus.Open;
        CreatedAt = now;
        UpdatedAt = now;
    }

    // EF Core
    private StaffTask()
        : base(default)
    {
        SubjectType = string.Empty;
    }

    public StaffTaskKind Kind { get; private set; }

    public SiteId? SiteId { get; private set; }

    public string SubjectType { get; private set; }

    public Guid SubjectId { get; private set; }

    public PersonId? AssignedToId { get; private set; }

    public StaffTaskStatus Status { get; private set; }

    public StaffTaskResolutionAction? ResolutionAction { get; private set; }

    public string? ResolutionNote { get; private set; }

    public PersonId? ResolvedById { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public static StaffTask Open(StaffTaskKind kind, SiteId? siteId, string subjectType, Guid subjectId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectType);
        var task = new StaffTask(StaffTaskId.New(), kind, siteId, subjectType, subjectId, now);
        task.Raise(new StaffTaskCreated(task.Id, kind, siteId, now));
        return task;
    }

    public Result Claim(PersonId by, DateTimeOffset now)
    {
        if (Status == StaffTaskStatus.Resolved)
        {
            return InvalidTransition();
        }

        Status = StaffTaskStatus.InProgress;
        AssignedToId = by;
        UpdatedAt = now;
        return Result.Success();
    }

    public Result Resolve(PersonId? by, StaffTaskResolutionAction? action, string? note, DateTimeOffset now)
    {
        if (Status == StaffTaskStatus.Resolved)
        {
            return InvalidTransition();
        }

        if (action is null)
        {
            return Error.Validation("task.resolution_required", "Resolving a task requires a resolution action.");
        }

        if (note is { Length: > 500 })
        {
            return Error.Validation("task.resolution_required", "The resolution note is limited to 500 characters.");
        }

        Status = StaffTaskStatus.Resolved;
        ResolutionAction = action;
        ResolutionNote = note;
        ResolvedById = by;
        ResolvedAt = now;
        UpdatedAt = now;
        Raise(new StaffTaskResolved(Id, by, action.Value.ToString(), now));
        return Result.Success();
    }

    private static Error InvalidTransition() =>
        Error.Conflict("task.invalid_transition", "The task is already resolved.");
}
