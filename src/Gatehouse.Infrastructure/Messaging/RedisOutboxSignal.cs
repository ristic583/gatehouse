using Gatehouse.Domain.Common;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Gatehouse.Infrastructure.Messaging;

/// <summary>
/// Pending-outbox signal in Redis, one key per tenant (<c>tenant:{tenantId}:outbox:pending</c>). The writer sets the key after its
/// commit; the relay takes it with GETDEL before it reads the outbox, so a row committed before the SET is always seen by that read and
/// a SET after the GETDEL stays for the next tick.
/// </summary>
public sealed partial class RedisOutboxSignal(IConnectionMultiplexer redis, ILogger<RedisOutboxSignal> logger) : IOutboxSignal
{
    /// <summary>Keys of tenants that are never relayed again (deleted, suspended) expire by themselves.</summary>
    public static readonly TimeSpan KeyLifetime = TimeSpan.FromHours(1);

    public static RedisKey Key(TenantId tenant) => $"tenant:{tenant.Value}:outbox:pending";

    public void Notify(TenantId tenant)
    {
        try
        {
            redis.GetDatabase().StringSet(Key(tenant), 1, KeyLifetime, flags: CommandFlags.FireAndForget);
        }
        catch (RedisException ex)
        {
            LogNotifyFailed(ex, tenant.Value);
        }
    }

    public async Task<IReadOnlySet<TenantId>?> TakeAsync(IReadOnlyCollection<TenantId> tenants, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tenants);
        try
        {
            var db = redis.GetDatabase();
            var batch = db.CreateBatch();
            var takes = tenants.Select(t => (Tenant: t, Value: batch.StringGetDeleteAsync(Key(t)))).ToList();
            batch.Execute();
            await Task.WhenAll(takes.Select(t => t.Value)).WaitAsync(ct);
            return takes.Where(t => t.Value.Result.HasValue).Select(t => t.Tenant).ToHashSet();
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            LogTakeFailed(ex);
            return null;
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning, Message = "Outbox signal for tenant {TenantId} not written; the relay sweep picks the rows up")]
    private partial void LogNotifyFailed(Exception ex, string tenantId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox signals unavailable; relaying every tenant this tick")]
    private partial void LogTakeFailed(Exception ex);
}
