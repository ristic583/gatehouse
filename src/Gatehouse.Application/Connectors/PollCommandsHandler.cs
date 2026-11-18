using System.Text.Json;
using Gatehouse.Application.Tenancy;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Commands;
using Gatehouse.Connector.Contracts.Events;
using Gatehouse.Connector.Contracts.Protocol;
using Gatehouse.Domain.Common;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.Application.Connectors;

/// <summary>
/// <c>POST /connector/v1/poll</c> long-poll loop: one fresh tenant scope per iteration so each select
/// (<see cref="ConnectorCommandDelivery"/>) sees current data. Delivered <c>SetPin</c> commands carrying a <c>vault:</c> reference
/// are re-encrypted to the polling agent's certificate in the response only; without an RSA client certificate they are failed with
/// <c>pin.transport_unavailable</c> and not delivered.
/// </summary>
public sealed class PollCommandsHandler(
    ITenantScopeFactory scopes,
    IConnectorPollGate gate,
    IConnectorCommandSignal signal,
    TimeProvider time)
{
    private static readonly TimeSpan MaxWaitSlice = TimeSpan.FromSeconds(2);

    public async Task<Result<PollResponse>> HandleAsync(PollCommandsCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var (connector, maxBatch, waitSeconds) = (command.Connector, command.MaxBatch, command.WaitSeconds);
        ArgumentNullException.ThrowIfNull(connector);
        var leaseTime = TimeSpan.FromSeconds(waitSeconds + 5);
        await using var lease = await gate.TryAcquireAsync(connector.TenantId, connector.PacsInstanceId, leaseTime, ct);
        if (lease is null)
        {
            return Error.Conflict("connector.poll_in_progress", "Another poll is open for this PACS instance.");
        }

        var started = time.GetTimestamp();
        var deadline = TimeSpan.FromSeconds(waitSeconds);
        while (true)
        {
            var now = time.GetUtcNow();
            IReadOnlyList<CommandEnvelope> commands;
            await using (var scope = await scopes.CreateScopeAsync(connector.TenantId, ct))
            {
                var inner = scope.ServiceProvider.GetRequiredService<ConnectorContext>();
                inner.Set(connector.TenantId, connector.PacsInstanceId, connector.ConnectorId);
                inner.ClientCertificate = connector.ClientCertificate;
                var delivery = scope.ServiceProvider.GetRequiredService<ConnectorCommandDelivery>();
                commands = await delivery.SelectAndDeliverAsync(inner, maxBatch, now, ct);
                commands = await SecurePinsAsync(scope.ServiceProvider, inner, commands, ct);
            }

            var remaining = deadline - time.GetElapsedTime(started);
            if (commands.Count > 0 || remaining <= TimeSpan.Zero || ct.IsCancellationRequested)
            {
                return new PollResponse(now, commands);
            }

            try
            {
                var slice = remaining < MaxWaitSlice ? remaining : MaxWaitSlice;
                await signal.WaitAsync(connector.TenantId, connector.PacsInstanceId, slice, ct);
            }
            catch (OperationCanceledException)
            {
                return new PollResponse(time.GetUtcNow(), []);
            }
        }
    }

    private static async Task<IReadOnlyList<CommandEnvelope>> SecurePinsAsync(
        IServiceProvider services, ConnectorContext connector, IReadOnlyList<CommandEnvelope> commands, CancellationToken ct)
    {
        if (!commands.Any(c => c.Type == ConnectorCommandTypes.SetPin))
        {
            return commands;
        }

        var encryptor = services.GetRequiredService<IPinTransportEncryptor>();
        var delivered = new List<CommandEnvelope>(commands.Count);
        var failed = new List<CommandAcked>();
        foreach (var command in commands)
        {
            if (command.Type != ConnectorCommandTypes.SetPin)
            {
                delivered.Add(command);
                continue;
            }

            var payload = ConnectorJson.DeserializePayload<SetPin>(command);
            if (!payload.EncryptedPin.StartsWith(Credentials.IPinVault.TransportPrefix, StringComparison.Ordinal))
            {
                delivered.Add(command);
                continue;
            }

            var encrypted = connector.ClientCertificate is { } certificate
                ? await encryptor.EncryptForAgentAsync(payload.EncryptedPin, certificate, ct)
                : null;
            if (encrypted is null)
            {
                failed.Add(new CommandAcked(command.CommandId, command.IdempotencyKey, AckStatus.Failed, null, null,
                    new AckError("pin.transport_unavailable", "No RSA client certificate to encrypt the PIN to.", false)));
                continue;
            }

            var securedPayload = JsonSerializer.SerializeToElement(payload with { EncryptedPin = encrypted }, ConnectorJson.Options);
            delivered.Add(command with { Payload = securedPayload });
        }

        if (failed.Count > 0)
        {
            await services.GetRequiredService<AckCommandsHandler>().HandleAsync(new AckRequest(failed), ct);
        }

        return delivered;
    }
}
