using Gatehouse.Application.Common.Messaging;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Infrastructure.Messaging;
using Gatehouse.Infrastructure.Messaging.Outbox;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Messaging;

/// <summary>GH-CONN-04: only the processed_messages duplicate is swallowed; business unique violations are retried.</summary>
public sealed class ConsumerTransactionTests(PostgresFixture postgres) : IAsyncLifetime
{
    private const string Consumer = "ConsumerTransactionProbe";
    private readonly GatehouseApiFactory _factory = new(postgres);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => _factory.InitializeAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Business_unique_violation_is_rethrown()
    {
        var instanceId = await InstanceAsync();
        await using (var seed = await _factory.CreateTenantScopeAsync("nordlys"))
        {
            var db = seed.ServiceProvider.GetRequiredService<TenantDbContext>();
            db.ConnectorCommands.Add(Command(instanceId, "probe:dup", 1));
            await db.SaveChangesAsync(Ct);
        }

        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var context = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var messageId = Guid.NewGuid();

        await Assert.ThrowsAsync<DbUpdateException>(() => ConsumerTransaction.RunAsync(
            context, scope.ServiceProvider.GetRequiredService<IMessageDeduplicator>(), messageId, Consumer,
            () =>
            {
                context.ConnectorCommands.Add(Command(instanceId, "probe:dup", 2));
                return Task.CompletedTask;
            },
            Ct));

        await using var check = await _factory.CreateTenantScopeAsync("nordlys");
        Assert.False(await check.ServiceProvider.GetRequiredService<TenantDbContext>().ProcessedMessages.AnyAsync(m => m.MessageId == messageId, Ct));
    }

    [Fact]
    public async Task Concurrent_duplicate_delivery_is_swallowed_and_not_committed()
    {
        var instanceId = await InstanceAsync();
        var messageId = Guid.NewGuid();
        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var context = scope.ServiceProvider.GetRequiredService<TenantDbContext>();

        var committed = await ConsumerTransaction.RunAsync(
            context, scope.ServiceProvider.GetRequiredService<IMessageDeduplicator>(), messageId, Consumer,
            async () =>
            {
                // Another delivery of the same message commits first.
                await using var other = await _factory.CreateTenantScopeAsync("nordlys");
                var db = other.ServiceProvider.GetRequiredService<TenantDbContext>();
                db.ProcessedMessages.Add(new ProcessedMessage { MessageId = messageId, Consumer = Consumer, ProcessedAt = DateTimeOffset.UtcNow });
                await db.SaveChangesAsync(Ct);
                context.ConnectorCommands.Add(Command(instanceId, "probe:loser", 3));
            },
            Ct);

        Assert.False(committed);
        await using var check = await _factory.CreateTenantScopeAsync("nordlys");
        Assert.False(await check.ServiceProvider.GetRequiredService<TenantDbContext>().ConnectorCommands.AnyAsync(c => c.IdempotencyKey == "probe:loser", Ct));
    }

    private static ConnectorCommand Command(PacsInstanceId instanceId, string key, long sequence) =>
        ConnectorCommand.Create(instanceId, "UpsertCardholder", "probe:partition", sequence, CommandPriority.Normal, key, "{}", null, DateTimeOffset.UtcNow);

    private async Task<PacsInstanceId> InstanceAsync()
    {
        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        return (await scope.ServiceProvider.GetRequiredService<TenantDbContext>().PacsInstances.AsNoTracking().FirstAsync(Ct)).Id;
    }
}
