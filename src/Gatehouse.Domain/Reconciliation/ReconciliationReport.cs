using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Reconciliation.Events;

namespace Gatehouse.Domain.Reconciliation;

/// <summary>One reconciliation run of a PACS instance (domain-model.md §2.7). Its id is also the export id.</summary>
public sealed class ReconciliationReport : AggregateRoot<ReconciliationReportId>
{
    private ReconciliationReport(
        ReconciliationReportId id, PacsInstanceId pacsInstanceId, PersonId? requestedById, CommandId commandId, DateTimeOffset now)
        : base(id)
    {
        PacsInstanceId = pacsInstanceId;
        RequestedById = requestedById;
        CommandId = commandId;
        RequestedAt = now;
        Status = ReconciliationStatus.Requested;
    }

    // EF Core
    private ReconciliationReport()
        : base(default)
    {
    }

    public PacsInstanceId PacsInstanceId { get; private set; }

    public PersonId? RequestedById { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    public ReconciliationStatus Status { get; private set; }

    public CommandId CommandId { get; private set; }

    public int ReceivedChunkCount { get; private set; }

    public DateTimeOffset? CapturedAt { get; private set; }

    public DateTimeOffset? ComparedAt { get; private set; }

    public ReconciliationCoverage? Coverage { get; private set; }

    public string? FailureCode { get; private set; }

    public string? FailureMessage { get; private set; }

    public uint Version { get; private set; }

    public bool IsRunning => Status is ReconciliationStatus.Requested or ReconciliationStatus.Exporting or ReconciliationStatus.Comparing;

    /// <summary>Creates a report; <paramref name="commandId"/> is the <c>ExportRecords</c> command that carries this id as export
    /// id.</summary>
    public static ReconciliationReport Request(
        PacsInstanceId pacsInstanceId, PersonId? requestedById, CommandId commandId, DateTimeOffset now) =>
        Request(ReconciliationReportId.New(), pacsInstanceId, requestedById, commandId, now);

    /// <summary>Creates a report with a known id (the handler builds the export command before the report).</summary>
    public static ReconciliationReport Request(
        ReconciliationReportId id, PacsInstanceId pacsInstanceId, PersonId? requestedById, CommandId commandId, DateTimeOffset now)
    {
        var report = new ReconciliationReport(id, pacsInstanceId, requestedById, commandId, now);
        report.Raise(new ReconciliationRequested(report.Id, pacsInstanceId, requestedById, now));
        return report;
    }

    public Result<ChunkAcceptance> AcceptChunk(int chunkIndex, DateTimeOffset capturedAt, bool isLast)
    {
        if (Status is ReconciliationStatus.Completed or ReconciliationStatus.Failed)
        {
            return InvalidTransition();
        }

        if (chunkIndex < ReceivedChunkCount)
        {
            return ChunkAcceptance.Duplicate;
        }

        if (Status == ReconciliationStatus.Comparing)
        {
            return InvalidTransition();
        }

        if (chunkIndex > ReceivedChunkCount)
        {
            return ChunkAcceptance.OutOfOrder;
        }

        if (chunkIndex == 0)
        {
            CapturedAt = capturedAt;
        }

        ReceivedChunkCount++;
        Status = isLast ? ReconciliationStatus.Comparing : ReconciliationStatus.Exporting;
        return ChunkAcceptance.Accepted;
    }

    public Result Complete(ReconciliationCoverage coverage, int findingCount, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(coverage);
        if (Status != ReconciliationStatus.Comparing)
        {
            return InvalidTransition();
        }

        Status = ReconciliationStatus.Completed;
        Coverage = coverage;
        ComparedAt = now;
        Raise(new ReconciliationCompleted(Id, coverage.ComparedRecordCount, findingCount, now));
        return Result.Success();
    }

    public Result Fail(string code, string message, DateTimeOffset now)
    {
        if (!IsRunning)
        {
            return InvalidTransition();
        }

        Status = ReconciliationStatus.Failed;
        FailureCode = code;
        FailureMessage = message;
        Raise(new ReconciliationFailed(Id, code, now));
        return Result.Success();
    }

    private Error InvalidTransition() =>
        Error.Conflict("reconciliation.invalid_transition", $"The reconciliation report is {Status}.");
}
