using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using Gatehouse.Application.Connectors;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Protocol;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Connectors;

public sealed class PollEndpointTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatewayFactory _factory = new(postgres);

    public async ValueTask InitializeAsync()
    {
        await _factory.InitializeAsync();
        await ConnectorTestEnrollment.ResetAsync(_factory);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Delivers_upsert_before_grant_one_per_partition()
    {
        await SeedAsync(Cmd("UpsertCardholder", "person:a", 1), Cmd("GrantAccess", "person:a", 2), Cmd("UpsertCardholder", "person:b", 1));
        using var cert = await EnrollAsync();
        var response = await PollAsync(cert);
        Assert.Equal(2, response.Commands.Count);
        Assert.Equal(["UpsertCardholder", "UpsertCardholder"], response.Commands.Select(c => c.Type));
        Assert.Equal(1, response.Commands.Single(c => c.PartitionKey == "person:a").Sequence);
    }

    [Fact]
    public async Task Busy_partition_blocks_next_command()
    {
        await SeedAsync(Cmd("UpsertCardholder", "person:a", 1), Cmd("GrantAccess", "person:a", 2));
        using var cert = await EnrollAsync();
        Assert.Single((await PollAsync(cert)).Commands);
        Assert.Empty((await PollAsync(cert)).Commands);
    }

    [Fact]
    public async Task High_priority_is_delivered_first_and_ignores_busy_partition()
    {
        await SeedAsync(Cmd("UpsertCardholder", "person:a", 1));
        using var cert = await EnrollAsync();
        Assert.Single((await PollAsync(cert)).Commands);
        await SeedAsync(Cmd("GrantAccess", "person:b", 1), Cmd("RevokeAccess", "person:a", 2, CommandPriority.High));
        var response = await PollAsync(cert);
        Assert.Equal(["RevokeAccess", "GrantAccess"], response.Commands.Select(c => c.Type));
    }

    [Fact]
    public async Task Expired_lease_redelivers_same_command_id()
    {
        await SeedAsync(Cmd("UpsertCardholder", "person:a", 1));
        using var cert = await EnrollAsync();
        var first = Assert.Single((await PollAsync(cert)).Commands);
        await UpdateAsync(c => c.SetProperty(x => x.LeaseUntil, DateTimeOffset.UtcNow.AddMinutes(-1)));
        var second = Assert.Single((await PollAsync(cert)).Commands);
        Assert.Equal(first.CommandId, second.CommandId);
        Assert.Equal(first.IdempotencyKey, second.IdempotencyKey);
        Assert.Equal(2, (await LoadAsync()).Single().Attempts);
    }

    [Fact]
    public async Task Fifth_lease_expiry_fails_command()
    {
        await SeedAsync(Cmd("UpsertCardholder", "person:a", 1));
        using var cert = await EnrollAsync();
        Assert.Single((await PollAsync(cert)).Commands);
        await UpdateAsync(c => c
            .SetProperty(x => x.Attempts, ConnectorCommand.MaxAttempts)
            .SetProperty(x => x.LeaseUntil, DateTimeOffset.UtcNow.AddMinutes(-1)));
        Assert.Empty((await PollAsync(cert)).Commands);
        var command = Assert.Single(await LoadAsync());
        Assert.Equal(ConnectorCommandStatus.Failed, command.Status);
        Assert.Equal("connector.lease_expired", command.LastError);
        await using var scope = await ConnectorTestEnrollment.ScopeAsync(_factory, "nordlys");
        Assert.True(await scope.ServiceProvider.GetRequiredService<TenantDbContext>().OutboxMessages.AnyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Delivery_marks_fulfillment_pending()
    {
        var ct = TestContext.Current.CancellationToken;
        Guid fid;
        Guid pacsId;
        await using (var scope = await ConnectorTestEnrollment.ScopeAsync(_factory, "nordlys"))
        {
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            pacsId = (await db.PacsInstances.AsNoTracking().SingleAsync(p => p.Name == "HQ-Copenhagen", ct)).Id.Value;
            var used = await db.Fulfillments.AsNoTracking().Where(f => f.Kind == FulfillmentKind.Revoke).Select(f => f.EntitlementId).ToListAsync(ct);
            var entitlement = await db.Entitlements.AsNoTracking().FirstAsync(e => !used.Contains(e.Id), ct);
            var fulfillment = Gatehouse.Domain.Fulfillment.Fulfillment.Create(entitlement.Id, new PacsInstanceId(pacsId), FulfillmentKind.Revoke, CommandPriority.Normal, DateTimeOffset.UtcNow);
            db.Fulfillments.Add(fulfillment);
            db.ConnectorCommands.Add(ConnectorCommand.Create(new PacsInstanceId(pacsId), "GrantAccess", "person:a", 1, CommandPriority.Normal, "k1", "{}", fulfillment.Id, DateTimeOffset.UtcNow));
            await db.SaveChangesAsync(ct);
            fid = fulfillment.Id.Value;
        }

        using var cert = await EnrollAsync();
        var delivered = Assert.Single((await PollAsync(cert)).Commands);
        await using var check = await ConnectorTestEnrollment.ScopeAsync(_factory, "nordlys");
        var stored = await check.ServiceProvider.GetRequiredService<TenantDbContext>().Fulfillments.AsNoTracking().SingleAsync(f => f.Id == new FulfillmentId(fid), ct);
        Assert.Equal(FulfillmentStatus.Pending, stored.Status);
        Assert.Equal(delivered.CommandId, stored.CommandId!.Value.Value);
    }

    [Fact]
    public async Task Backoff_blocks_partition_until_next_attempt()
    {
        await SeedAsync(Cmd("UpsertCardholder", "person:a", 1));
        await UpdateAsync(c => c.SetProperty(x => x.NextAttemptAt, DateTimeOffset.UtcNow.AddMinutes(5)));
        using var cert = await EnrollAsync();
        Assert.Empty((await PollAsync(cert)).Commands);
        await UpdateAsync(c => c.SetProperty(x => x.NextAttemptAt, DateTimeOffset.UtcNow.AddSeconds(-1)));
        Assert.Single((await PollAsync(cert)).Commands);
    }

    [Fact]
    public async Task Concurrent_poll_returns_409()
    {
        using var cert = await EnrollAsync();
        var open = PollAsync(cert, waitSeconds: 3);
        await Task.Delay(700, TestContext.Current.CancellationToken);
        using var client = _factory.CreateClient();
        var second = await ConnectorTestEnrollment.PostAsync(client, "/connector/v1/poll", new PollRequest(10, 0), cert);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Contains("connector.poll_in_progress", await second.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        await open;
    }

    [Fact]
    public async Task Empty_poll_returns_after_wait()
    {
        using var cert = await EnrollAsync();
        var watch = Stopwatch.StartNew();
        var response = await PollAsync(cert, waitSeconds: 1);
        Assert.Empty(response.Commands);
        Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(900), watch.Elapsed.ToString());
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(6));
    }

    [Fact]
    public async Task Signal_wakes_waiting_poll()
    {
        var ct = TestContext.Current.CancellationToken;
        using var cert = await EnrollAsync();
        var watch = Stopwatch.StartNew();
        var pending = PollAsync(cert, waitSeconds: 20);
        await Task.Delay(800, ct);
        var pacsId = await SeedAsync(Cmd("UpsertCardholder", "person:a", 1));
        await _factory.Services.GetRequiredService<IConnectorCommandSignal>().NotifyAsync(new TenantId("nordlys"), new PacsInstanceId(pacsId), ct);
        var response = await pending;
        Assert.Single(response.Commands);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(8), watch.Elapsed.ToString());
    }

    public static Func<PacsInstanceId, ConnectorCommand> Cmd(string type, string partition, long sequence, CommandPriority priority = CommandPriority.Normal) =>
        pacs => ConnectorCommand.Create(pacs, type, partition, sequence, priority, $"{type}:{partition}:{sequence}", "{}", null, DateTimeOffset.UtcNow.AddMilliseconds(sequence));

    public async Task<Guid> SeedAsync(params Func<PacsInstanceId, ConnectorCommand>[] commands)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = await ConnectorTestEnrollment.ScopeAsync(_factory, "nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        var pacs = await db.PacsInstances.AsNoTracking().SingleAsync(p => p.Name == "HQ-Copenhagen", ct);
        db.ConnectorCommands.AddRange(commands.Select(c => c(pacs.Id)));
        await db.SaveChangesAsync(ct);
        return pacs.Id.Value;
    }

    private Task<X509Certificate2> EnrollAsync() => ConnectorTestEnrollment.EnrollAsync(_factory, "nordlys", "HQ-Copenhagen");

    private async Task<PollResponse> PollAsync(X509Certificate2 cert, int waitSeconds = 0)
    {
        using var client = _factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(40);
        var response = await ConnectorTestEnrollment.PostAsync(client, "/connector/v1/poll", new PollRequest(50, waitSeconds), cert);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<PollResponse>(ConnectorJson.Options, TestContext.Current.CancellationToken))!;
    }

    private async Task UpdateAsync(Action<Microsoft.EntityFrameworkCore.Query.UpdateSettersBuilder<ConnectorCommand>> setters)
    {
        await using var scope = await ConnectorTestEnrollment.ScopeAsync(_factory, "nordlys");
        await scope.ServiceProvider.GetRequiredService<TenantDbContext>().ConnectorCommands.ExecuteUpdateAsync(setters, TestContext.Current.CancellationToken);
    }

    private async Task<List<ConnectorCommand>> LoadAsync()
    {
        await using var scope = await ConnectorTestEnrollment.ScopeAsync(_factory, "nordlys");
        return await scope.ServiceProvider.GetRequiredService<TenantDbContext>().ConnectorCommands.AsNoTracking().ToListAsync(TestContext.Current.CancellationToken);
    }
}
