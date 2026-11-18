using System.Collections.Concurrent;
using Gatehouse.Application.Common.Messaging;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Infrastructure.Messaging;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Messaging;

public sealed record ProbeMessage(string Name);

public sealed class ProbeConsumer(ITenantContext tenant, TenantDbContext db, IMessageDeduplicator dedup) : IConsumer<ProbeMessage>
{
    public static string? SeenTenant { get; set; }

    // Per message id: test classes run in parallel and share this consumer, so a global counter races.
    private static readonly ConcurrentDictionary<Guid, int> Runs = new();

    public static int RunsOf(Guid messageId) => Runs.GetValueOrDefault(messageId);

    public Task Consume(ConsumeContext<ProbeMessage> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        SeenTenant = tenant.TenantId.Value;
        var messageId = context.MessageId!.Value;
        return ConsumerTransaction.RunAsync(db, dedup, messageId, nameof(ProbeConsumer), () =>
        {
            Runs.AddOrUpdate(messageId, 1, (_, runs) => runs + 1);
            return Task.CompletedTask;
        }, context.CancellationToken);
    }
}

public sealed class TenantScopeConsumeFilterTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatehouseApiFactory _factory = new(postgres);

    public ValueTask InitializeAsync() => _factory.InitializeAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Message_without_tenant_header_goes_to_error()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        await using var driver = await MessagingDriver.CreateAsync(_factory.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>(), x => x.AddConsumer<ProbeConsumer>());
        await driver.Harness.Bus.Publish(new ProbeMessage("x"), Pipe.Execute<PublishContext>(c => c.MessageId = id), ct);

        Assert.True(await driver.Harness.Consumed.Any<ProbeMessage>(m => m.Exception is MissingTenantHeaderException, ct));
        Assert.Equal(0, ProbeConsumer.RunsOf(id));
    }

    [Fact]
    public async Task Consumer_sees_tenant_from_header()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var driver = await MessagingDriver.CreateAsync(_factory.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>(), x => x.AddConsumer<ProbeConsumer>());
        await driver.Harness.Bus.Publish(new ProbeMessage("x"), Pipe.Execute<PublishContext>(c => c.Headers.Set("gh-tenant-id", "nordlys")), ct);

        Assert.True(await driver.Harness.Consumed.Any<ProbeMessage>(ct));
        await driver.Harness.InactivityTask;
        Assert.Equal("nordlys", ProbeConsumer.SeenTenant);
    }
}
