using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;

namespace Gatehouse.Domain.Connectors;

public sealed class ConnectorCommand : AggregateRoot<CommandId>
{
    public const int MaxAttempts = 5;

    public static readonly TimeSpan LeaseDuration = TimeSpan.FromSeconds(120);

    private static readonly TimeSpan MaxBackoff = TimeSpan.FromMinutes(5);

    private ConnectorCommand(
        CommandId id,
        PacsInstanceId pacsInstanceId,
        string type,
        string partitionKey,
        long sequence,
        CommandPriority priority,
        string idempotencyKey,
        string payloadJson,
        FulfillmentId? fulfillmentId,
        DateTimeOffset now)
        : base(id)
    {
        PacsInstanceId = pacsInstanceId;
        Type = type;
        PartitionKey = partitionKey;
        Sequence = sequence;
        Priority = priority;
        IdempotencyKey = idempotencyKey;
        PayloadJson = payloadJson;
        FulfillmentId = fulfillmentId;
        Status = ConnectorCommandStatus.Queued;
        NextAttemptAt = now;
        CreatedAt = now;
    }

    // EF Core
    private ConnectorCommand()
        : base(default)
    {
        Type = string.Empty;
        PartitionKey = string.Empty;
        IdempotencyKey = string.Empty;
        PayloadJson = string.Empty;
    }

    public PacsInstanceId PacsInstanceId { get; private set; }

    public string Type { get; private set; }

    public string PartitionKey { get; private set; }

    public long Sequence { get; private set; }

    public CommandPriority Priority { get; private set; }

    public string IdempotencyKey { get; private set; }

    public string PayloadJson { get; private set; }

    public ConnectorCommandStatus Status { get; private set; }

    public int Attempts { get; private set; }

    public DateTimeOffset NextAttemptAt { get; private set; }

    public DateTimeOffset? LeaseUntil { get; private set; }

    public DateTimeOffset? DeliveredAt { get; private set; }

    public DateTimeOffset? AckedAt { get; private set; }

    public string? LastError { get; private set; }

    public string? PacsRef { get; private set; }

    public FulfillmentId? FulfillmentId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public uint Version { get; private set; }

    /// <summary>W3C traceparent of the activity that created the command (M9); delivered to the agent in the poll envelope.</summary>
    public string? TraceParent { get; private set; }

    public void AttachTraceParent(string? traceParent) => TraceParent = traceParent is { Length: <= 55 } ? traceParent : null;

    public static ConnectorCommand Create(
        PacsInstanceId pacsInstanceId,
        string type,
        string partitionKey,
        long sequence,
        CommandPriority priority,
        string idempotencyKey,
        string payloadJson,
        FulfillmentId? fulfillmentId,
        DateTimeOffset now) =>
        new(CommandId.New(), pacsInstanceId, type, partitionKey, sequence, priority, idempotencyKey, payloadJson, fulfillmentId, now);

    public Result Deliver(DateTimeOffset now)
    {
        if (Status != ConnectorCommandStatus.Queued)
        {
            return Error.Conflict("connector.invalid_command_state", $"Command is {Status}, not Queued.");
        }

        Status = ConnectorCommandStatus.Delivered;
        LeaseUntil = now + LeaseDuration;
        DeliveredAt = now;
        Attempts++;
        return Result.Success();
    }

    public CommandOutcome ExpireLease(DateTimeOffset now)
    {
        if (Status != ConnectorCommandStatus.Delivered || LeaseUntil is not { } lease || lease > now)
        {
            return CommandOutcome.None;
        }

        LeaseUntil = null;
        if (Attempts >= MaxAttempts)
        {
            Status = ConnectorCommandStatus.Failed;
            LastError = "connector.lease_expired";
            return CommandOutcome.Failed;
        }

        Status = ConnectorCommandStatus.Queued;
        NextAttemptAt = now;
        return CommandOutcome.Requeued;
    }

    public CommandOutcome ApplyAck(
        CommandAckStatus status,
        string? pacsRef,
        string? code,
        string? message,
        bool retryable,
        DateTimeOffset now)
    {
        if (Status is ConnectorCommandStatus.Acked or ConnectorCommandStatus.Failed)
        {
            var sameAsFinal = (status == CommandAckStatus.Applied) == (Status == ConnectorCommandStatus.Acked);
            return sameAsFinal ? CommandOutcome.Duplicate : CommandOutcome.Conflict;
        }

        var accepted = Status == ConnectorCommandStatus.Delivered
            || (Status == ConnectorCommandStatus.Queued && Attempts > 0);
        if (!accepted)
        {
            return CommandOutcome.Conflict;
        }

        LeaseUntil = null;
        if (status == CommandAckStatus.Applied)
        {
            Status = ConnectorCommandStatus.Acked;
            PacsRef = pacsRef;
            AckedAt = now;
            LastError = null;
            return CommandOutcome.Acked;
        }

        LastError = string.IsNullOrEmpty(code) ? message : code;
        if (status == CommandAckStatus.Failed && retryable && Attempts < MaxAttempts)
        {
            Status = ConnectorCommandStatus.Queued;
            NextAttemptAt = now + Backoff(Attempts);
            return CommandOutcome.RetryScheduled;
        }

        Status = ConnectorCommandStatus.Failed;
        return CommandOutcome.Failed;
    }

    /// <summary>
    /// Holds a never-delivered command until <paramref name="notBefore"/> (a grant whose window starts later, for a PACS without native
    /// validity: GH-CONN-01). A deferred command does not block its partition.
    /// </summary>
    public Result DeferUntil(DateTimeOffset notBefore)
    {
        if (Status != ConnectorCommandStatus.Queued || Attempts > 0)
        {
            return Error.Conflict("connector.invalid_command_state", "Only a never-delivered queued command can be deferred.");
        }

        if (notBefore > NextAttemptAt)
        {
            NextAttemptAt = notBefore;
        }

        return Result.Success();
    }

    /// <summary>Makes a deferred command due now (the PACS turned out to enforce validity windows itself).</summary>
    public void ReleaseDeferral(DateTimeOffset now)
    {
        if (IsDeferred(now))
        {
            NextAttemptAt = now;
        }
    }

    /// <summary>True for a never-delivered queued command held until a later time (<see cref="DeferUntil"/>).</summary>
    public bool IsDeferred(DateTimeOffset now) => Status == ConnectorCommandStatus.Queued && Attempts == 0 && NextAttemptAt > now;

    /// <summary>
    /// Fails a queued command that must not be delivered any more (an interactive door command past its execute-by deadline: GH-LIVE-01).
    /// </summary>
    public bool Expire(string code, DateTimeOffset now)
    {
        if (Status != ConnectorCommandStatus.Queued)
        {
            return false;
        }

        Status = ConnectorCommandStatus.Failed;
        LeaseUntil = null;
        LastError = code;
        NextAttemptAt = now;
        return true;
    }

    public bool Supersede(DateTimeOffset now)
    {
        if (Status != ConnectorCommandStatus.Queued)
        {
            return false;
        }

        Status = ConnectorCommandStatus.Superseded;
        LeaseUntil = null;
        return true;
    }

    private static TimeSpan Backoff(int attempts)
    {
        var seconds = 5d * Math.Pow(2, Math.Max(0, attempts - 1));
        var span = TimeSpan.FromSeconds(seconds);
        return span > MaxBackoff ? MaxBackoff : span;
    }
}
