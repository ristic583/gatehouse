using Gatehouse.Application.Audit;
using Gatehouse.Cli;
using Gatehouse.Infrastructure;
using Gatehouse.Infrastructure.Platform;
using Microsoft.Extensions.Hosting;

var parsed = CliArguments.Parse(args);
if (parsed.IsFailure)
{
    await Console.Error.WriteLineAsync(parsed.Error.Message);
    await Console.Error.WriteLineAsync(CliArguments.Usage);
    return CliRunner.UsageError;
}

if (parsed.Value is CliCommand.Help)
{
    Console.WriteLine(CliArguments.Usage);
    return CliRunner.Success;
}

var builder = Host.CreateApplicationBuilder();
builder.AddGatehouseCatalog();
builder.Services.AddGatehousePlatform();
builder.AddGatehouseTenancy();
builder.Services.AddAuditModule();

using var host = builder.Build();
return await new CliRunner(host.Services, Console.Out).RunAsync(parsed.Value, CancellationToken.None);
