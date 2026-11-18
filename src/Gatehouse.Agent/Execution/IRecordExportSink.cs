using Gatehouse.Connector.Contracts.Events;

namespace Gatehouse.Agent.Execution;

public interface IRecordExportSink
{
    public Task SendAsync(RecordExport chunk, CancellationToken ct);
}
