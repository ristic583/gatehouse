using Gatehouse.Domain.Access;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment.Events;
using Gatehouse.Domain.People;

namespace Gatehouse.Domain.Fulfillment;

public sealed class Fulfillment : AggregateRoot<FulfillmentId>
{
    private Fulfillment(
        FulfillmentId id,
        EntitlementId entitlementId,
        PacsInstanceId pacsInstanceId,
        FulfillmentKind kind,
        CommandPriority priority,
        DateTimeOffset now)
        : base(id)
    {
        EntitlementId = entitlementId;
        PacsInstanceId = pacsInstanceId;
        Kind = kind;
        Priority = priority;
        Status = FulfillmentStatus.Desired;
        Revision = 1;
        CreatedAt = now;
        UpdatedAt = now;
    }

    // EF Core
    private Fulfillment()
        : base(default)
    {
    }

    public EntitlementId EntitlementId { get; private set; }

    public PacsInstanceId PacsInstanceId { get; private set; }

    public FulfillmentKind Kind { get; private set; }

    public CommandPriority Priority { get; private set; }

    public FulfillmentStatus Status { get; private set; }

    public int Revision { get; private set; }

    public CommandId? CommandId { get; private set; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    public string? PacsRef { get; private set; }

    public ConfirmerKind? ConfirmedByKind { get; private set; }

    public string? ConfirmedById { get; private set; }

    public DateTimeOffset? ConfirmedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public uint Version { get; private set; }

    public static Fulfillment Create(
        EntitlementId entitlementId,
        PacsInstanceId pacsInstanceId,
        FulfillmentKind kind,
        CommandPriority priority,
        DateTimeOffset now)
    {
        var fulfillment = new Fulfillment(FulfillmentId.New(), entitlementId, pacsInstanceId, kind, priority, now);
        fulfillment.RaiseDesired(now);
        return fulfillment;
    }

    /// <summary>
    /// A revoke removes the assignment now although another entitlement's window on the same level starts later (PACS without native
    /// validity): the grant of that remainder is its own <c>Grant</c> fulfillment, so its failure follows the normal failed-fulfillment
    /// path instead of hiding behind this already confirmed revoke (GH-CONN-01).
    /// </summary>
    public Result<Fulfillment> PlanDeferredRegrant(EntitlementId remainderEntitlementId, DateTimeOffset now)
    {
        if (Kind != FulfillmentKind.Revoke)
        {
            return InvalidTransition();
        }

        return Create(remainderEntitlementId, PacsInstanceId, FulfillmentKind.Grant, CommandPriority.Normal, now);
    }

    public Result MarkPending(CommandId? commandId, DateTimeOffset now)
    {
        if (Status != FulfillmentStatus.Desired)
        {
            return InvalidTransition();
        }

        Status = FulfillmentStatus.Pending;
        CommandId = commandId;
        Attempts++;
        UpdatedAt = now;
        if (commandId is { } id)
        {
            Raise(new FulfillmentDispatched(Id, id, Attempts, now));
        }

        return Result.Success();
    }

    public Result ConfirmByAgent(string connectorId, string? pacsRef, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectorId);
        if (Status != FulfillmentStatus.Pending)
        {
            return InvalidTransition();
        }

        return Confirm(ConfirmerKind.Agent, connectorId, pacsRef, now);
    }

    public Result ConfirmByStaff(PersonId staffId, DateTimeOffset now)
    {
        if (Status is not (FulfillmentStatus.Pending or FulfillmentStatus.Desired))
        {
            return InvalidTransition();
        }

        return Confirm(ConfirmerKind.Staff, staffId.Value.ToString(), PacsRef, now);
    }

    public Result Fail(string code, string message, DateTimeOffset now)
    {
        if (Status is not (FulfillmentStatus.Pending or FulfillmentStatus.Desired))
        {
            return InvalidTransition();
        }

        Status = FulfillmentStatus.Failed;
        LastError = code + ": " + message;
        UpdatedAt = now;
        Raise(new FulfillmentFailed(Id, EntitlementId, Kind, code, message, Attempts, now));
        return Result.Success();
    }

    public Result Retry(DateTimeOffset now)
    {
        if (Status != FulfillmentStatus.Failed)
        {
            return InvalidTransition();
        }

        ResetToDesired(now);
        return Result.Success();
    }

    /// <summary>
    /// Applies a settled or in-flight fulfillment again with what is mapped now (new revision, raises <c>FulfillmentDesired</c>): a mapping
    /// edit moves a confirmed grant to the new level, and a revoke already used for that move removes again (GH-ACCESS-01). There is one
    /// fulfillment per entitlement, instance and kind, so it is re-opened instead of created. The priority only rises.
    /// </summary>
    public Result Reapply(CommandPriority priority, DateTimeOffset now)
    {
        if (Status is not (FulfillmentStatus.Confirmed or FulfillmentStatus.Pending))
        {
            return InvalidTransition();
        }

        if (priority > Priority)
        {
            Priority = priority;
        }

        ResetToDesired(now);
        return Result.Success();
    }

    public Result DetectDrift(Guid? findingId, DateTimeOffset now)
    {
        if (Status != FulfillmentStatus.Confirmed)
        {
            return InvalidTransition();
        }

        Status = FulfillmentStatus.Drift;
        UpdatedAt = now;
        Raise(new FulfillmentDriftDetected(Id, EntitlementId, findingId, now));
        return Result.Success();
    }

    public Result Remediate(DateTimeOffset now)
    {
        if (Status != FulfillmentStatus.Drift)
        {
            return InvalidTransition();
        }

        ResetToDesired(now);
        return Result.Success();
    }

    public Result ResolveAsInSync(PersonId staffId, DateTimeOffset now)
    {
        if (Status != FulfillmentStatus.Drift)
        {
            return InvalidTransition();
        }

        _ = staffId;
        Status = FulfillmentStatus.Confirmed;
        UpdatedAt = now;
        return Result.Success();
    }

    public Result Supersede(DateTimeOffset now)
    {
        if (Status is not (FulfillmentStatus.Desired or FulfillmentStatus.Pending or FulfillmentStatus.Failed))
        {
            return InvalidTransition();
        }

        Status = FulfillmentStatus.Superseded;
        UpdatedAt = now;
        Raise(new FulfillmentSuperseded(Id, EntitlementId, Kind, now));
        return Result.Success();
    }

    public Result FlagRevocationUnconfirmed(DateTimeOffset revokedAt, DateTimeOffset now)
    {
        if (Kind != FulfillmentKind.Revoke || Status is FulfillmentStatus.Confirmed or FulfillmentStatus.Superseded)
        {
            return InvalidTransition();
        }

        Raise(new RevocationUnconfirmedAlert(EntitlementId, Id, revokedAt, now - revokedAt, now));
        return Result.Success();
    }

    private static Error InvalidTransition() =>
        Error.Conflict("fulfillment.invalid_transition", "The fulfillment is not in a state that allows this change.");

    private Result Confirm(ConfirmerKind kind, string confirmedById, string? pacsRef, DateTimeOffset now)
    {
        Status = FulfillmentStatus.Confirmed;
        ConfirmedByKind = kind;
        ConfirmedById = confirmedById;
        ConfirmedAt = now;
        PacsRef = pacsRef;
        LastError = null;
        UpdatedAt = now;
        Raise(new FulfillmentConfirmed(Id, EntitlementId, Kind, kind, confirmedById, now, pacsRef, now));
        return Result.Success();
    }

    private void ResetToDesired(DateTimeOffset now)
    {
        Revision++;
        Status = FulfillmentStatus.Desired;
        CommandId = null;
        Attempts = 0;
        UpdatedAt = now;
        RaiseDesired(now);
    }

    private void RaiseDesired(DateTimeOffset now) =>
        Raise(new FulfillmentDesired(Id, EntitlementId, PacsInstanceId, Kind, Priority, Revision, now));
}
