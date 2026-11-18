using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using Gatehouse.Application.Audit;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Events;
using Gatehouse.Connector.Contracts.Protocol;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Connectors;

public sealed class AckEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatewayFactory _factory = new(postgres);
    private MessagingDriver? _messaging;

    public async ValueTask InitializeAsync()
    {
        await _factory.InitializeAsync();
        await ConnectorTestEnrollment.ResetAsync(_factory);
        await using (var scope = await ConnectorTestEnrollment.ScopeAsync(_factory, "nordlys"))
        {
            // Earlier suites may leave queued commands behind; these tests expect an empty queue.
            await scope.ServiceProvider.GetRequiredService<TenantDbContext>().ConnectorCommands.ExecuteDeleteAsync(CancellationToken.None);
        }

        _messaging = await MessagingDriver.CreateAsync(_factory.Services.GetRequiredService<IConfiguration>());
    }

    public async ValueTask DisposeAsync()
    {
        if (_messaging is not null)
        {
            await _messaging.DisposeAsync();
        }

        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Applied_ack_confirms_fulfillment_by_agent_after_drain()
    {
        using var cert = await EnrollAsync();
        var (fid, command) = await DeliveredAsync(cert);
        var result = await AckAsync(cert, Applied(command));
        Assert.True(Assert.Single(result.Results).Accepted);
        await DrainUntilAsync(async () => (await FulfillmentAsync(fid)).Status == FulfillmentStatus.Confirmed);
        var f = await FulfillmentAsync(fid);
        Assert.Equal(ConfirmerKind.Agent, f.ConfirmedByKind);
    }

    [Fact]
    public async Task Retryable_failure_requeues_with_backoff()
    {
        using var cert = await EnrollAsync();
        var (fid, command) = await DeliveredAsync(cert);
        await AckAsync(cert, Failed(command, retryable: true));
        var stored = Assert.Single(await CommandsAsync());
        Assert.Equal(ConnectorCommandStatus.Queued, stored.Status);
        Assert.True(stored.NextAttemptAt > DateTimeOffset.UtcNow.AddSeconds(2));
        Assert.Equal(FulfillmentStatus.Pending, (await FulfillmentAsync(fid)).Status);
    }

    [Fact]
    public async Task Fifth_retryable_failure_fails_fulfillment_and_opens_task()
    {
        using var cert = await EnrollAsync();
        var (fid, command) = await DeliveredAsync(cert);
        await SetAttemptsAsync(ConnectorCommand.MaxAttempts);
        await AckAsync(cert, Failed(command, retryable: true));
        Assert.Equal(ConnectorCommandStatus.Failed, Assert.Single(await CommandsAsync()).Status);
        await DrainUntilAsync(async () => await TaskExistsAsync(fid));
        Assert.Equal(FulfillmentStatus.Failed, (await FulfillmentAsync(fid)).Status);
    }

    [Fact]
    public async Task Non_retryable_failure_fails_fulfillment_and_opens_task()
    {
        using var cert = await EnrollAsync();
        var (fid, command) = await DeliveredAsync(cert);
        await AckAsync(cert, Failed(command, retryable: false));
        await DrainUntilAsync(async () => await TaskExistsAsync(fid));
        Assert.Equal(FulfillmentStatus.Failed, (await FulfillmentAsync(fid)).Status);
    }

    [Fact]
    public async Task Duplicate_ack_is_accepted_once()
    {
        using var cert = await EnrollAsync();
        var (_, command) = await DeliveredAsync(cert);
        await using (var scope = await ConnectorTestEnrollment.ScopeAsync(_factory, "nordlys"))
        {
            await scope.ServiceProvider.GetRequiredService<TenantDbContext>().OutboxMessages.ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        }

        for (var i = 0; i < 2; i++)
        {
            Assert.True(Assert.Single((await AckAsync(cert, Applied(command))).Results).Accepted);
        }

        await using var check = await ConnectorTestEnrollment.ScopeAsync(_factory, "nordlys");
        Assert.Equal(1, await check.ServiceProvider.GetRequiredService<TenantDbContext>().OutboxMessages.CountAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Conflicting_final_ack_returns_already_final()
    {
        using var cert = await EnrollAsync();
        var (_, command) = await DeliveredAsync(cert);
        await AckAsync(cert, Applied(command));
        var item = Assert.Single((await AckAsync(cert, Failed(command, retryable: false))).Results);
        Assert.False(item.Accepted);
        Assert.Equal("connector.already_final", item.Code);
    }

    [Fact]
    public async Task Ack_for_other_instance_command_is_unknown_command()
    {
        var ct = TestContext.Current.CancellationToken;
        Guid otherId;
        await using (var scope = await ConnectorTestEnrollment.ScopeAsync(_factory, "bricks"))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var pacs = await db.PacsInstances.AsNoTracking().FirstAsync(ct);
            var other = ConnectorCommand.Create(pacs.Id, "UpsertCardholder", "person:x", 1, CommandPriority.Normal, "x", "{}", null, DateTimeOffset.UtcNow);
            db.ConnectorCommands.Add(other);
            await db.SaveChangesAsync(ct);
            otherId = other.Id.Value;
        }

        using var cert = await EnrollAsync();
        var item = Assert.Single((await AckAsync(cert, new CommandAcked(otherId, "x", AckStatus.Applied, null, DateTimeOffset.UtcNow, null))).Results);
        Assert.False(item.Accepted);
        Assert.Equal("connector.unknown_command", item.Code);
    }

    [Fact]
    public async Task Confirmed_fulfillment_has_agent_audit_entry()
    {
        var ct = TestContext.Current.CancellationToken;
        using var cert = await EnrollAsync();
        var (fid, command) = await DeliveredAsync(cert);
        await AckAsync(cert, Applied(command));
        await DrainUntilAsync(async () => (await FulfillmentAsync(fid)).Status == FulfillmentStatus.Confirmed);
        await using var scope = await ConnectorTestEnrollment.ScopeAsync(_factory, "nordlys");
        var entry = await scope.ServiceProvider.GetRequiredService<TenantDbContext>().AuditEntries.AsNoTracking()
            .SingleAsync(a => a.Action == "fulfillment.confirmed" && a.SubjectId == fid.ToString(), ct);
        Assert.Equal(AuditActorKind.Agent, entry.ActorKind);
    }

    private static CommandAcked Applied(ConnectorCommand c) => new(c.Id.Value, c.IdempotencyKey, AckStatus.Applied, "ref-1", DateTimeOffset.UtcNow, null);

    private static CommandAcked Failed(ConnectorCommand c, bool retryable) =>
        new(c.Id.Value, c.IdempotencyKey, AckStatus.Failed, null, null, new AckError("adapter.timeout", "PACS timed out", retryable));

    private Task<X509Certificate2> EnrollAsync() => ConnectorTestEnrollment.EnrollAsync(_factory, "nordlys", "HQ-Copenhagen");

    private async Task<(Guid FulfillmentId, ConnectorCommand Command)> DeliveredAsync(X509Certificate2 cert)
    {
        var ct = TestContext.Current.CancellationToken;
        Guid fid;
        await using (var scope = await ConnectorTestEnrollment.ScopeAsync(_factory, "nordlys"))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            var pacs = await db.PacsInstances.AsNoTracking().SingleAsync(p => p.Name == "HQ-Copenhagen", ct);
            var used = await db.Fulfillments.AsNoTracking().Where(f => f.Kind == FulfillmentKind.Revoke).Select(f => f.EntitlementId).ToListAsync(ct);
            var entitlement = await db.Entitlements.AsNoTracking().FirstAsync(e => !used.Contains(e.Id), ct);
            var fulfillment = Gatehouse.Domain.Fulfillment.Fulfillment.Create(entitlement.Id, pacs.Id, FulfillmentKind.Revoke, CommandPriority.Normal, DateTimeOffset.UtcNow);
            db.Fulfillments.Add(fulfillment);
            db.ConnectorCommands.Add(ConnectorCommand.Create(pacs.Id, "RevokeAccess", "person:a", 1, CommandPriority.Normal, "RevokeAccess:k:1", "{}", fulfillment.Id, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(ct);
            fid = fulfillment.Id.Value;
        }

        using var client = _factory.CreateClient();
        var response = await ConnectorTestEnrollment.PostAsync(client, "/connector/v1/poll", new PollRequest(50, 0), cert);
        response.EnsureSuccessStatusCode();
        Assert.Single((await response.Content.ReadFromJsonAsync<PollResponse>(ConnectorJson.Options, ct))!.Commands);
        return (fid, Assert.Single(await CommandsAsync()));
    }

    private async Task<AckResponse> AckAsync(X509Certificate2 cert, params CommandAcked[] acks)
    {
        using var client = _factory.CreateClient();
        var response = await ConnectorTestEnrollment.PostAsync(client, "/connector/v1/acks", new AckRequest(acks), cert);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AckResponse>(ConnectorJson.Options, TestContext.Current.CancellationToken))!;
    }

    private async Task DrainUntilAsync(Func<Task<bool>> done)
    {
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < deadline)
        {
            await _messaging!.DrainAsync(new TenantId("nordlys"), CancellationToken.None);
            if (await done())
            {
                return;
            }

            await Task.Delay(250, TestContext.Current.CancellationToken);
        }
    }

    private async Task SetAttemptsAsync(int attempts)
    {
        await using var scope = await ConnectorTestEnrollment.ScopeAsync(_factory, "nordlys");
        await scope.ServiceProvider.GetRequiredService<TenantDbContext>().ConnectorCommands
            .ExecuteUpdateAsync(c => c.SetProperty(x => x.Attempts, attempts), TestContext.Current.CancellationToken);
    }

    private async Task<bool> TaskExistsAsync(Guid fid)
    {
        await using var scope = await ConnectorTestEnrollment.ScopeAsync(_factory, "nordlys");
        return await scope.ServiceProvider.GetRequiredService<TenantDbContext>().StaffTasks.AsNoTracking()
            .AnyAsync(t => t.SubjectType == AuditSubjects.Fulfillment && t.SubjectId == fid, TestContext.Current.CancellationToken);
    }

    private async Task<Gatehouse.Domain.Fulfillment.Fulfillment> FulfillmentAsync(Guid fid)
    {
        await using var scope = await ConnectorTestEnrollment.ScopeAsync(_factory, "nordlys");
        return await scope.ServiceProvider.GetRequiredService<TenantDbContext>().Fulfillments.AsNoTracking()
            .SingleAsync(f => f.Id == new FulfillmentId(fid), TestContext.Current.CancellationToken);
    }

    private async Task<List<ConnectorCommand>> CommandsAsync()
    {
        await using var scope = await ConnectorTestEnrollment.ScopeAsync(_factory, "nordlys");
        return await scope.ServiceProvider.GetRequiredService<TenantDbContext>().ConnectorCommands.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
    }
}
