using System.Text.Json;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Common;
using Gatehouse.Infrastructure.Messaging.Outbox;
using Gatehouse.Infrastructure.Persistence.Tenant;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Gatehouse.Infrastructure.Messaging;

/// <summary>Publishes pending tenant outbox rows to the bus, high priority first (ADR-005).</summary>
public sealed partial class OutboxRelay(
    ITenantScopeFactory scopes,
    OutboxMessageTypeRegistry registry,
    TimeProvider time,
    ILogger<OutboxRelay> logger)
{
    private const int BatchSize = 100;

    public async Task<int> RunOnceAsync(TenantId tenantId, CancellationToken ct) =>
        (await RelayAsync(tenantId, ct)).Published;

    /// <summary>Publishes one batch; reports when the earliest remaining row is due (<see cref="OutboxRelayScheduler"/>).</summary>
    public async Task<OutboxRelayOutcome> RelayAsync(TenantId tenantId, CancellationToken ct)
    {
        await using var scope = await scopes.CreateScopeAsync(tenantId, ct);
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var publisher = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var now = time.GetUtcNow();
        var rows = await db.Set<OutboxMessage>()
            .FromSqlInterpolated($"""
                SELECT * FROM outbox_messages
                WHERE dispatched_at IS NULL AND next_attempt_at <= {now}
                ORDER BY CASE priority WHEN 'High' THEN 0 ELSE 1 END, occurred_at
                LIMIT {BatchSize} FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);

        var published = 0;
        foreach (var row in rows)
        {
            try
            {
                using var span = StartPublishActivity(row.Type, row.TraceParent);
                var type = registry.Resolve(row.Type);
                var message = JsonSerializer.Deserialize(row.Payload, type, OutboxJson.Options)!;
                await publisher.Publish(message, type, Pipe.Execute<PublishContext>(ctx =>
                {
                    ctx.MessageId = row.Id;
                    ctx.Headers.Set("gh-tenant-id", tenantId.Value);
                }), ct);
                row.DispatchedAt = now;
                published++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
#pragma warning disable CA1031 // relay boundary: any publish failure is logged, recorded on the row and retried with backoff
            catch (Exception ex)
#pragma warning restore CA1031
            {
                row.Attempts++;
                row.LastError = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
                row.NextAttemptAt = now + TimeSpan.FromSeconds(Math.Min(Math.Pow(2, row.Attempts - 1), 300));
                LogPublishFailed(ex, tenantId.Value, row.Id, row.Attempts);
            }
        }

        await db.SaveChangesAsync(ct);
        var nextAttemptAt = await db.Set<OutboxMessage>()
            .Where(m => m.DispatchedAt == null)
            .MinAsync(m => (DateTimeOffset?)m.NextAttemptAt, ct);
        await tx.CommitAsync(ct);
        return new OutboxRelayOutcome(published, nextAttemptAt);
    }

    /// <summary>The publish span for an outbox row, child of the request that wrote it; MassTransit propagates it to consumers.</summary>
    public static System.Diagnostics.Activity? StartPublishActivity(string type, string? traceParent) =>
        Gatehouse.Application.Common.Diagnostics.GatehouseTelemetry.StartWithParent(
            $"outbox publish {type}", traceParent, System.Diagnostics.ActivityKind.Producer);

    [LoggerMessage(
        Level = LogLevel.Warning, Message = "Outbox publish failed for tenant {TenantId} message {MessageId} (attempt {Attempts})")]
    private partial void LogPublishFailed(Exception ex, string tenantId, Guid messageId, int attempts);
}
