using Gatehouse.Agent.Transport;
using Gatehouse.Connector.Contracts.Events;
using Microsoft.Extensions.Logging;

namespace Gatehouse.Agent.Execution;

/// <summary>
/// Uploads export chunks to <c>POST /connector/v1/exports</c>, retrying transient failures (network, 429, 5xx) with
/// <see cref="Backoff"/> until cancelled. Logs chunk index and record count only.
/// </summary>
public sealed partial class GatewayExportSink(
    GatewayClient client, TimeProvider time, ILogger<GatewayExportSink> logger, Backoff? backoff = null)
    : IRecordExportSink
{
    public const string OutOfOrderCode = "connector.export_chunk_out_of_order";

    private readonly Backoff _backoff = backoff ?? new Backoff();

    public async Task SendAsync(RecordExport chunk, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        _backoff.Reset();
        var index = chunk.ChunkIndex;
        var count = chunk.Records.Length;
        while (true)
        {
            try
            {
                await client.SendExportAsync(chunk, ct);
                LogChunkAccepted(index, count);
                return;
            }
            catch (GatewayTransientException ex)
            {
                var delay = _backoff.Next(ex.RetryAfter);
                logger.LogWarning("Export chunk {ChunkIndex} upload failed transiently; retrying in {Delay}", index, delay);
                await Task.Delay(delay, time, ct);
            }
            catch (GatewayRejectedException ex) when (ex.Code == OutOfOrderCode)
            {
                logger.LogWarning("Export chunk {ChunkIndex} out of order; restarting the export", index);
                throw new ExportRestartRequiredException();
            }
            catch (GatewayRejectedException ex)
            {
                logger.LogError("Export chunk {ChunkIndex} rejected with {Code}", index, ex.Code);
                throw new ExportRejectedException(ex.Code ?? "connector.export_rejected");
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Export chunk {ChunkIndex} with {RecordCount} records accepted")]
    private partial void LogChunkAccepted(int chunkIndex, int recordCount);
}
