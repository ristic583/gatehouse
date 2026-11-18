using Gatehouse.Cli;
using Gatehouse.Domain.Common;
using Gatehouse.Infrastructure.Persistence.Catalog;
using Gatehouse.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.IntegrationTests.Cli;

public sealed class CliConnectorTests(PostgresFixture postgres) : IAsyncLifetime
{
    private readonly GatehouseApiFactory _factory = new(postgres);

    public async ValueTask InitializeAsync()
    {
        await _factory.InitializeAsync();
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<CatalogDbContext>().ConnectorRegistrations.ExecuteDeleteAsync(CancellationToken.None);
    }

    public ValueTask DisposeAsync() => _factory.DisposeAsync();

    [Fact]
    public async Task Connector_create_prints_token_once()
    {
        var ct = TestContext.Current.CancellationToken;
        var command = CliArguments.Parse(["connector", "create", "--tenant", "nordlys", "--pacs", "HQ-Copenhagen"]);
        Assert.True(command.IsSuccess);
        using var output = new StringWriter();
        var code = await new CliRunner(_factory.Services, output).RunAsync(command.Value, ct);
        Assert.Equal(CliRunner.Success, code);
        var text = output.ToString();
        Assert.Equal(1, text.Split("ghe_").Length - 1);
        var again = new StringWriter();
        Assert.Equal(CliRunner.OperationFailed, await new CliRunner(_factory.Services, again).RunAsync(command.Value, ct));
        Assert.DoesNotContain("ghe_", again.ToString(), StringComparison.Ordinal);
    }
}
