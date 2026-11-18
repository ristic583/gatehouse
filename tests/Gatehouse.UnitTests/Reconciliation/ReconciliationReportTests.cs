using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Reconciliation;
using Gatehouse.Domain.Reconciliation.Events;

namespace Gatehouse.UnitTests.Reconciliation;

public sealed class ReconciliationReportTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 8, 0, 0, TimeSpan.Zero);

    private static readonly ReconciliationCoverage Coverage = new([Guid.NewGuid()], [Guid.NewGuid()], ["L1"], ["AccessAssignment"], 5, 4, 3, 1, 1);

    private static ReconciliationReport New() => ReconciliationReport.Request(PacsInstanceId.New(), PersonId.New(), CommandId.New(), Now);

    private static ReconciliationReport Comparing()
    {
        var report = New();
        Assert.Equal(ChunkAcceptance.Accepted, report.AcceptChunk(0, Now, true).Value);
        report.ClearDomainEvents();
        return report;
    }

    [Fact]
    public void Request_raises_requested_event()
    {
        var instanceId = PacsInstanceId.New();
        var report = ReconciliationReport.Request(instanceId, null, CommandId.New(), Now);
        Assert.Equal(ReconciliationStatus.Requested, report.Status);
        Assert.True(report.IsRunning);
        var requested = Assert.IsType<ReconciliationRequested>(Assert.Single(report.DomainEvents));
        Assert.Equal(report.Id, requested.ReportId);
        Assert.Equal(instanceId, requested.PacsInstanceId);
        Assert.Null(requested.RequestedById);
    }

    [Fact]
    public void First_chunk_moves_to_exporting_and_sets_captured_at()
    {
        var report = New();
        var captured = Now.AddMinutes(1);
        Assert.Equal(ChunkAcceptance.Accepted, report.AcceptChunk(0, captured, false).Value);
        Assert.Equal(ReconciliationStatus.Exporting, report.Status);
        Assert.Equal(captured, report.CapturedAt);
        Assert.Equal(1, report.ReceivedChunkCount);

        Assert.Equal(ChunkAcceptance.Accepted, report.AcceptChunk(1, captured.AddSeconds(5), false).Value);
        Assert.Equal(captured, report.CapturedAt);
    }

    [Fact]
    public void Last_chunk_moves_to_comparing()
    {
        var report = New();
        report.AcceptChunk(0, Now, false);
        Assert.Equal(ChunkAcceptance.Accepted, report.AcceptChunk(1, Now, true).Value);
        Assert.Equal(ReconciliationStatus.Comparing, report.Status);
        Assert.Equal(2, report.ReceivedChunkCount);
    }

    [Fact]
    public void Duplicate_chunk_is_ignored()
    {
        var report = New();
        report.AcceptChunk(0, Now, false);
        Assert.Equal(ChunkAcceptance.Duplicate, report.AcceptChunk(0, Now.AddMinutes(1), false).Value);
        Assert.Equal(1, report.ReceivedChunkCount);
        Assert.Equal(Now, report.CapturedAt);

        report.AcceptChunk(1, Now, true);
        Assert.Equal(ChunkAcceptance.Duplicate, report.AcceptChunk(1, Now, true).Value);
        Assert.Equal(ReconciliationStatus.Comparing, report.Status);
    }

    [Fact]
    public void Skipping_a_chunk_is_out_of_order()
    {
        var report = New();
        Assert.Equal(ChunkAcceptance.OutOfOrder, report.AcceptChunk(1, Now, false).Value);
        Assert.Equal(ReconciliationStatus.Requested, report.Status);
        Assert.Equal(0, report.ReceivedChunkCount);
    }

    [Fact]
    public void Complete_only_from_comparing()
    {
        var exporting = New();
        exporting.AcceptChunk(0, Now, false);
        Assert.Equal("reconciliation.invalid_transition", exporting.Complete(Coverage, 0, Now).Error.Code);

        var report = Comparing();
        Assert.True(report.Complete(Coverage, 2, Now.AddMinutes(2)).IsSuccess);
        Assert.Equal(ReconciliationStatus.Completed, report.Status);
        Assert.Equal(Now.AddMinutes(2), report.ComparedAt);
        Assert.Same(Coverage, report.Coverage);
        Assert.False(report.IsRunning);
        var completed = Assert.IsType<ReconciliationCompleted>(Assert.Single(report.DomainEvents));
        Assert.Equal(Coverage.ComparedRecordCount, completed.ComparedRecords);
        Assert.Equal(2, completed.FindingCount);
        Assert.Equal("reconciliation.invalid_transition", report.AcceptChunk(1, Now, true).Error.Code);
    }

    [Fact]
    public void Fail_from_completed_is_invalid()
    {
        var report = Comparing();
        report.Complete(Coverage, 0, Now);
        Assert.Equal("reconciliation.invalid_transition", report.Fail("reconciliation.timed_out", "Timed out.", Now).Error.Code);
        Assert.Equal(ReconciliationStatus.Completed, report.Status);

        var running = New();
        running.ClearDomainEvents();
        Assert.True(running.Fail("reconciliation.timed_out", "Timed out.", Now).IsSuccess);
        Assert.Equal(ReconciliationStatus.Failed, running.Status);
        Assert.Equal("reconciliation.timed_out", running.FailureCode);
        var failed = Assert.IsType<ReconciliationFailed>(Assert.Single(running.DomainEvents));
        Assert.Equal("reconciliation.timed_out", failed.Code);
    }
}
