using Gatehouse.Domain.Common;
using Gatehouse.Domain.LiveBuilding.Events;
using Gatehouse.Domain.People;

namespace Gatehouse.Domain.LiveBuilding;

public sealed class DoorCommand : AggregateRoot<DoorCommandId>
{
    public const int MaxReasonLength = 500;
    public const int MaxUnlockSeconds = 60;

    private DoorCommand(
        DoorCommandId id,
        DoorCommandKind kind,
        DoorCommandScope scope,
        Guid scopeId,
        PersonId requestedById,
        string reason,
        int? durationSeconds,
        int doorCount,
        DateTimeOffset requestedAt)
        : base(id)
    {
        Kind = kind;
        Scope = scope;
        ScopeId = scopeId;
        RequestedById = requestedById;
        Reason = reason;
        DurationSeconds = durationSeconds;
        DoorCount = doorCount;
        RequestedAt = requestedAt;
    }

    // EF Core
    private DoorCommand()
        : base(default)
    {
        Reason = string.Empty;
    }

    public DoorCommandKind Kind { get; private set; }

    public DoorCommandScope Scope { get; private set; }

    public Guid ScopeId { get; private set; }

    public PersonId RequestedById { get; private set; }

    public string Reason { get; private set; }

    public int? DurationSeconds { get; private set; }

    public int DoorCount { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    public static Result<DoorCommand> Request(
        DoorCommandKind kind,
        DoorCommandScope scope,
        Guid scopeId,
        PersonId requestedById,
        string? reason,
        int? durationSeconds,
        int doorCount,
        DateTimeOffset now)
    {
        var trimmed = reason?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxReasonLength)
        {
            return Error.Validation("door_command.reason_required", "A reason of 1-500 characters is required.");
        }

        var unlock = kind == DoorCommandKind.MomentaryUnlock;
        var durationValid = unlock
            ? durationSeconds is >= 1 and <= MaxUnlockSeconds && scope == DoorCommandScope.Door
            : durationSeconds is null;
        if (!durationValid)
        {
            return Error.Validation(
                "door_command.invalid_duration",
                "Momentary unlock needs a single door and 1-60 seconds; lockdown takes no duration.");
        }

        var command = new DoorCommand(DoorCommandId.New(), kind, scope, scopeId, requestedById, trimmed, durationSeconds, doorCount, now);
        command.Raise(new DoorCommandRequested(command.Id, kind, scope, scopeId, requestedById, trimmed, durationSeconds, now));
        return command;
    }
}
