using System.Text.Json;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Adapters;
using Gatehouse.Connector.Contracts.Commands;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.LiveBuilding;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Reconciliation;
using Gatehouse.Domain.Sites;

namespace Gatehouse.Application.Connectors;

/// <summary>Builds queued commands with payload, idempotency key <c>{Type}:{fulfillmentId}:{revision}</c> and partition
/// <c>person:{personId}</c>.</summary>
public sealed class ConnectorCommandFactory(IConnectorSequencer sequencer, TimeProvider time)
{
    public static string PartitionKey(PersonId personId) => $"person:{personId.Value}";

    public static string PartitionKey(DoorId doorId) => $"door:{doorId.Value}";

    public async Task<ConnectorCommand> CreateAsync(
        Domain.Fulfillment.Fulfillment fulfillment,
        PersonId personId,
        string type,
        object payload,
        FulfillmentId? linkedFulfillment,
        CommandPriority priority,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(fulfillment);
        ArgumentNullException.ThrowIfNull(payload);
        var partition = PartitionKey(personId);
        var sequence = await sequencer.NextAsync(fulfillment.PacsInstanceId, partition, ct);
        return Traced(ConnectorCommand.Create(
            fulfillment.PacsInstanceId,
            type,
            partition,
            sequence,
            priority,
            $"{type}:{fulfillment.Id.Value}:{fulfillment.Revision}",
            JsonSerializer.Serialize(payload, payload.GetType(), ConnectorJson.Options),
            linkedFulfillment,
            time.GetUtcNow()));
    }

    /// <summary>Door command: partition <c>door:{doorId}</c>, priority High, idempotency <c>{type}:{doorCommandId}:{doorId}</c>, no
    /// fulfillment.</summary>
    public async Task<ConnectorCommand> CreateForDoorAsync(
        PacsInstanceId instanceId,
        DoorCommandId doorCommandId,
        DoorId doorId,
        string type,
        object payload,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var partition = PartitionKey(doorId);
        var sequence = await sequencer.NextAsync(instanceId, partition, ct);
        return Traced(ConnectorCommand.Create(
            instanceId,
            type,
            partition,
            sequence,
            CommandPriority.High,
            $"{type}:{doorCommandId.Value}:{doorId.Value}",
            JsonSerializer.Serialize(payload, payload.GetType(), ConnectorJson.Options),
            null,
            time.GetUtcNow()));
    }

    /// <summary>Reconciliation export: partition <c>export:{reportId}</c>, priority Normal, idempotency <c>ExportRecords:{reportId}:1</c>,
    /// no fulfillment.</summary>
    public async Task<ConnectorCommand> CreateForExportAsync(
        PacsInstanceId instanceId,
        ReconciliationReportId reportId,
        IReadOnlyList<string> levelRefs,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(levelRefs);
        var partition = $"export:{reportId.Value}";
        var sequence = await sequencer.NextAsync(instanceId, partition, ct);
        var payload = new ExportRecords(reportId.Value, [RecordType.AccessAssignment], [.. levelRefs]);
        return Traced(ConnectorCommand.Create(
            instanceId,
            ConnectorCommandTypes.ExportRecords,
            partition,
            sequence,
            CommandPriority.Normal,
            $"{ConnectorCommandTypes.ExportRecords}:{reportId.Value}:1",
            JsonSerializer.Serialize(payload, ConnectorJson.Options),
            null,
            time.GetUtcNow()));
    }

    /// <summary>
    /// Removes a PACS assignment found by reconciliation (<c>RemovedFromPacs</c>): <c>RevokeAccess</c>, partition <c>person:{id}</c> for a
    /// known person else <c>finding:{findingId}</c>, idempotency <c>RevokeAccess:{findingId}:{attempt}</c>, priority Normal, no
    /// fulfillment.
    /// </summary>
    public async Task<ConnectorCommand> CreateForFindingRevokeAsync(
        PacsInstanceId instanceId, ReconciliationFinding finding, CancellationToken ct, int attempt = 1)
    {
        ArgumentNullException.ThrowIfNull(finding);
        var partition = finding.PersonId is { } personId ? PartitionKey(personId) : $"finding:{finding.Id.Value}";
        var sequence = await sequencer.NextAsync(instanceId, partition, ct);
        var payload = new RevokeAccess(
            finding.PersonRef ?? string.Empty, finding.PacsCardNumber ?? string.Empty, finding.VendorAccessLevelRef, RevocationKind.Normal);
        return Traced(ConnectorCommand.Create(
            instanceId,
            ConnectorCommandTypes.RevokeAccess,
            partition,
            sequence,
            CommandPriority.Normal,
            FindingRevokeKey(finding.Id, attempt),
            JsonSerializer.Serialize(payload, ConnectorJson.Options),
            null,
            time.GetUtcNow()));
    }

    /// <summary>Idempotency key of the <paramref name="attempt"/>-th removal of a reconciliation finding.</summary>
    public static string FindingRevokeKey(ReconciliationFindingId findingId, int attempt) =>
        $"{ConnectorCommandTypes.RevokeAccess}:{findingId.Value}:{attempt}";

    /// <summary>The finding id of a <see cref="FindingRevokeKey"/>, or null for any other key.</summary>
    public static ReconciliationFindingId? FindingOfRevokeKey(string idempotencyKey)
    {
        ArgumentNullException.ThrowIfNull(idempotencyKey);
        var parts = idempotencyKey.Split(':');
        return parts.Length == 3 && parts[0] == ConnectorCommandTypes.RevokeAccess && Guid.TryParse(parts[1], out var id)
            ? new ReconciliationFindingId(id)
            : null;
    }

    /// <summary>Card or PIN command for a person (M8): partition <c>person:{id}</c>, the given idempotency key, no fulfillment.</summary>
    public async Task<ConnectorCommand> CreateForPersonAsync(
        PacsInstanceId instanceId,
        PersonId personId,
        string type,
        object payload,
        string idempotencyKey,
        CommandPriority priority,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var partition = PartitionKey(personId);
        var sequence = await sequencer.NextAsync(instanceId, partition, ct);
        return Traced(ConnectorCommand.Create(
            instanceId,
            type,
            partition,
            sequence,
            priority,
            idempotencyKey,
            JsonSerializer.Serialize(payload, payload.GetType(), ConnectorJson.Options),
            null,
            time.GetUtcNow()));
    }

    private static ConnectorCommand Traced(ConnectorCommand command)
    {
        command.AttachTraceParent(Common.Diagnostics.GatehouseTelemetry.CurrentTraceParent());
        return command;
    }
}
