using System.Text.Json;
using Gatehouse.Agent.Storage;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Adapters;
using Gatehouse.Connector.Contracts.Commands;
using Gatehouse.Connector.Contracts.Events;
using Gatehouse.Connector.Contracts.Protocol;
using Microsoft.Extensions.Logging;

namespace Gatehouse.Agent.Execution;

public sealed partial class CommandExecutor(
    IPacsAdapter adapter,
    ProcessedCommandStore processed,
    IRecordExportSink exports,
    TimeProvider time,
    ILogger<CommandExecutor> logger,
    IPinDecryptor? pinDecryptor = null,
    ScheduledRevocationStore? schedules = null,
    FenceStore? fences = null)
{
    public const int ExportChunkSize = 1000;

    private const string HighPriority = "High";

    /// <summary>A grant whose window has ended is never applied by an adapter without native validity (GH-CONN-01).</summary>
    public const string GrantWindowEndedCode = "agent.grant_window_ended";

    /// <summary>A grant whose window has not started is not applied yet by an adapter without native validity (GH-CONN-01).</summary>
    public const string GrantNotYetValidCode = "agent.grant_not_yet_valid";

    /// <summary>A door command received after its execute-by deadline (GH-LIVE-01).</summary>
    public const string CommandExpiredCode = "agent.command_expired";

    /// <summary>A grant overtaken by a High priority revoke of the same person and access level (GH-CONN-02).</summary>
    public const string GrantFencedCode = "agent.grant_fenced";

    /// <summary>Tolerated clock difference between gateway and agent for validity-window starts.</summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(2);

    public async Task<CommandAcked> ExecuteAsync(CommandEnvelope envelope, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var stored = await processed.GetAsync(envelope.IdempotencyKey, ct);
        if (stored is not null)
        {
            LogAlreadyProcessed(envelope.Type, envelope.IdempotencyKey);
            return new CommandAcked(envelope.CommandId, envelope.IdempotencyKey, Enum.Parse<AckStatus>(
                stored.Status), stored.PacsRef, stored.ProcessedAt,
                stored.ErrorJson is null ? null : JsonSerializer.Deserialize<AckError>(stored.ErrorJson, ConnectorJson.Options));
        }

        AdapterResult result;
        try
        {
            result = await DispatchAsync(envelope, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Adapter threw for command {CommandType} {CommandId}", envelope.Type, envelope.CommandId);
            result = AdapterResult.Failed("adapter.unhandled_exception", ex.GetType().Name, true);
        }

        var now = time.GetUtcNow();
        var error = result.Error is null ? null : new AckError(result.Error.Code, result.Error.Message, result.Error.Retryable);
        var ack = new CommandAcked(
            envelope.CommandId, envelope.IdempotencyKey, Enum.Parse<AckStatus>(result.Status.ToString()), result.PacsRef, now, error);
        if (result.Status != AdapterStatus.Failed || result.Error is { Retryable: false })
        {
            await processed.SaveAsync(ack, error is null ? null : JsonSerializer.Serialize(error, ConnectorJson.Options), now, ct);
        }

        LogFinished(envelope.Type, envelope.CommandId, ack.Status);
        return ack;
    }

    private async Task<AdapterResult> DispatchAsync(CommandEnvelope e, CancellationToken ct)
    {
        switch (e.Type)
        {
            case ConnectorCommandTypes.UpsertCardholder:
                var u = ConnectorJson.DeserializePayload<UpsertCardholder>(e);
                return await adapter.UpsertCardholderAsync(
                    new Cardholder(u.PersonRef, u.GivenName, u.FamilyName, u.CardNumber, u.PhotoUrl), ct);
            case ConnectorCommandTypes.GrantAccess:
                var g = ConnectorJson.DeserializePayload<GrantAccess>(e);
                if (fences is not null && await fences.IsFencedAsync(g.PersonRef, g.VendorAccessLevelRef, e.Sequence, time.GetUtcNow(), ct))
                {
                    logger.LogWarning("Not granting {Level} for card {Card}: a High priority revoke with a higher sequence was received",
                        g.VendorAccessLevelRef, CardNumberMask.Mask(g.CardNumber));
                    return AdapterResult.Failed(
                        GrantFencedCode, "A High priority revoke of this access was received after the grant.", false);
                }

                if (!adapter.Capabilities.NativeValidity && WindowRefusal(g) is { } refused)
                {
                    logger.LogWarning(
                        "Not granting {Level} for card {Card}: {Code}", g.VendorAccessLevelRef, CardNumberMask.Mask(g.CardNumber),
                        refused.Error!.Code);
                    return refused;
                }

                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Granting {Level} for card {Card}", g.VendorAccessLevelRef, CardNumberMask.Mask(g.CardNumber));
                }

                var granted = await adapter.GrantAccessAsync(
                    new AccessGrant(g.PersonRef, g.CardNumber, g.VendorAccessLevelRef, g.ValidFrom, g.ValidTo), ct);
                if (granted.Status == AdapterStatus.Applied && !adapter.Capabilities.NativeValidity && schedules is not null)
                {
                    await schedules.ScheduleAsync(g.PersonRef, g.CardNumber, g.VendorAccessLevelRef, g.ValidTo, ct);
                }

                return granted;
            case ConnectorCommandTypes.RevokeAccess:
                var r = ConnectorJson.DeserializePayload<RevokeAccess>(e);
                if (fences is not null && string.Equals(e.Priority, HighPriority, StringComparison.Ordinal))
                {
                    // Recorded before the vendor call: a grant redelivered behind this revoke must not run even if the revoke fails.
                    await fences.RecordAsync(r.PersonRef, r.VendorAccessLevelRef, e.Sequence, time.GetUtcNow(), ct);
                }

                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Revoking {Level} for card {Card}", r.VendorAccessLevelRef, CardNumberMask.Mask(r.CardNumber));
                }

                var revoked = await adapter.RevokeAccessAsync(new AccessRevoke(r.PersonRef, r.CardNumber, r.VendorAccessLevelRef), ct);
                if (revoked.Status == AdapterStatus.Applied && schedules is not null)
                {
                    await schedules.CancelAsync(r.PersonRef, r.VendorAccessLevelRef, ct);
                }

                return revoked;
            case ConnectorCommandTypes.ActivateCard:
                var a = ConnectorJson.DeserializePayload<ActivateCard>(e);
                return await adapter.SetCardStateAsync(new CardStateChange(a.PersonRef, a.CardNumber, CardTargetState.Active), ct);
            case ConnectorCommandTypes.SuspendCard:
                var s = ConnectorJson.DeserializePayload<SuspendCard>(e);
                return await adapter.SetCardStateAsync(new CardStateChange(s.PersonRef, s.CardNumber, s.TargetState), ct);
            case ConnectorCommandTypes.SetPin:
                var p = ConnectorJson.DeserializePayload<SetPin>(e);
                if (!adapter.Capabilities.PinWrite)
                {
                    return AdapterResult.Unsupported("pinWrite");
                }

                return pinDecryptor is null
                    ? AdapterResult.Failed("agent.pin_decryption_unavailable", "No PIN decryptor configured", false)
                    : await adapter.SetPinAsync(new PinWrite(p.PersonRef, p.CardNumber, pinDecryptor.Decrypt(p.EncryptedPin)), ct);
            case ConnectorCommandTypes.MomentaryUnlock:
                var m = ConnectorJson.DeserializePayload<MomentaryUnlock>(e);
                // No deadline in the payload → issue time + the default deadline (PT-33): an unlock is never executed late.
                if (m.DeadlineOrDefault(e.IssuedAt) < time.GetUtcNow())
                {
                    return AdapterResult.Failed(CommandExpiredCode, "The unlock was received after its execute-by deadline.", false);
                }

                return await adapter.DoorCommandAsync(
                    new DoorCommand(DoorCommandKind.MomentaryUnlock, m.PacsDoorRefs, m.DurationSeconds, null, m.Reason), ct);
            case ConnectorCommandTypes.Lockdown:
                var l = ConnectorJson.DeserializePayload<Lockdown>(e);
                return await adapter.DoorCommandAsync(
                    new DoorCommand(DoorCommandKind.Lockdown, l.PacsDoorRefs, null, l.Action, l.Reason), ct);
            case ConnectorCommandTypes.ExportRecords:
                return await ExportAsync(e, ConnectorJson.DeserializePayload<ExportRecords>(e), ct);
            default:
                return AdapterResult.Unsupported("command:" + e.Type);
        }
    }

    /// <summary>The PACS cannot enforce the window itself, so the agent refuses to open access outside it.</summary>
    private AdapterResult? WindowRefusal(GrantAccess grant)
    {
        var now = time.GetUtcNow();
        if (grant.ValidTo <= now)
        {
            return AdapterResult.Failed(GrantWindowEndedCode, "The grant's validity window has already ended.", false);
        }

        return grant.ValidFrom > now + ClockSkew
            ? AdapterResult.Failed(GrantNotYetValidCode, "The grant's validity window has not started yet.", true)
            : null;
    }

    private async Task<AdapterResult> ExportAsync(CommandEnvelope e, ExportRecords x, CancellationToken ct)
    {
        if (!adapter.Capabilities.RecordExport)
        {
            return AdapterResult.Unsupported("recordExport");
        }

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await SendExportAsync(e, x, ct);
                return AdapterResult.Applied(null);
            }
            catch (ExportRestartRequiredException) when (attempt == 0)
            {
                logger.LogWarning("Restarting export {ExportId} from chunk 0", x.ExportId);
            }
            catch (ExportRestartRequiredException)
            {
                return AdapterResult.Failed(GatewayExportSink.OutOfOrderCode, "Export chunks were rejected as out of order twice", true);
            }
            catch (ExportRejectedException ex)
            {
                return AdapterResult.Failed(ex.Code, "The gateway rejected the export", false);
            }
        }
    }

    private async Task SendExportAsync(CommandEnvelope e, ExportRecords x, CancellationToken ct)
    {
        var capturedAt = time.GetUtcNow();
        var batch = new List<PacsRecord>();
        var index = 0;
        await foreach (var record in adapter.ExportRecordsAsync(new ExportScope(x.RecordTypes, x.VendorAccessLevelRefs), ct))
        {
            batch.Add(record);
            if (batch.Count >= ExportChunkSize)
            {
                await exports.SendAsync(new RecordExport(x.ExportId, e.CommandId, index++, false, capturedAt, [.. batch]), ct);
                batch.Clear();
            }
        }

        await exports.SendAsync(new RecordExport(x.ExportId, e.CommandId, index, true, capturedAt, [.. batch]), ct);
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Command {CommandType} {IdempotencyKey} already processed; re-sending stored ack")]
    private partial void LogAlreadyProcessed(string commandType, string idempotencyKey);

    [LoggerMessage(Level = LogLevel.Information, Message = "Command {CommandType} {CommandId} finished with {Status}")]
    private partial void LogFinished(string commandType, Guid commandId, AckStatus status);
}
