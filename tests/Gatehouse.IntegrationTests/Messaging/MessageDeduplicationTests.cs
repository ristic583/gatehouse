using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.IntegrationTests.Infrastructure;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Messaging;

public sealed class MessageDeduplicationTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatehouseApiFactory _factory = new(postgres);

    public ValueTask InitializeAsync() => _factory.InitializeAsync();

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Same_message_twice_is_processed_once()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        await using var driver = await MessagingDriver.CreateAsync(_factory.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>(), x => x.AddConsumer<ProbeConsumer>());
        for (var i = 0; i < 2; i++)
        {
            await driver.Harness.Bus.Publish(new ProbeMessage("x"), Pipe.Execute<PublishContext>(c =>
            {
                c.MessageId = id;
                c.Headers.Set("gh-tenant-id", "nordlys");
            }), ct);
            await driver.Harness.InactivityTask;
        }

        Assert.Equal(1, ProbeConsumer.RunsOf(id));
        await using var scope = await _factory.CreateTenantScopeAsync("nordlys");
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        Assert.Equal(1, await db.ProcessedMessages.CountAsync(m => m.MessageId == id, ct));
    }
}
