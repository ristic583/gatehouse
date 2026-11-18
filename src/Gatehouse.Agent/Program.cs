using Gatehouse.Adapters.CloudPacs;
using Gatehouse.Adapters.LegacyPacs;
using Gatehouse.Agent;
using Gatehouse.Agent.Storage;
using Gatehouse.Agent.Transport;
using Gatehouse.Connector.Contracts.Adapters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = Host.CreateApplicationBuilder(args);
var section = builder.Configuration.GetSection("Agent");
var options = new AgentOptions(
    section["GatewayUrl"] ?? "https://localhost:5443",
    section["DataDirectory"] ?? "agent-data",
    section["EnrollmentToken"],
    section["AgentVersion"] ?? "1.0.0",
    section["LegacyPacsConnectionString"] ?? string.Empty,
    string.Equals(section["TrustGatewayCertificate"], "dev", StringComparison.OrdinalIgnoreCase),
    section["Vendor"] ?? AgentOptions.LegacyPacsVendor,
    section["CloudPacsUrl"],
    section["CloudPacsApiKey"],
    section["WebhookListenUrl"] ?? AgentOptions.DefaultWebhookListenUrl,
    section["GatewayCertificatePath"],
    section["WebhookPublicUrl"]);

if (options.TrustDevGatewayCertificate && !builder.Environment.IsDevelopment())
{
    // PT-35: "dev" disables gateway authentication entirely; it must never reach a customer site.
    throw new InvalidOperationException(
        "Agent:TrustGatewayCertificate=dev is only allowed in the Development environment. Use Agent:GatewayCertificatePath instead.");
}

builder.Services.AddSingleton(options);
if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
{
    // Traces only, and only when an OTLP endpoint is configured (the AppHost sets one; customer sites opt in).
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService("gatehouse-agent"))
        .WithTracing(t => t.AddSource(AgentTelemetry.SourceName).AddHttpClientInstrumentation().AddOtlpExporter());
}

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new AgentDatabase(options.DataDirectory));
builder.Services.AddSingleton<IAdapterCursorStore, SqliteAdapterCursorStore>();
switch (options.Vendor)
{
    case AgentOptions.LegacyPacsVendor:
        builder.Services.AddSingleton<IPacsAdapter>(sp => new LegacyPacsAdapter(
            new LegacyPacsAdapterOptions(options.LegacyPacsConnectionString), sp.GetRequiredService<IAdapterCursorStore>()));
        break;
    case AgentOptions.CloudPacsVendor:
        if (!Uri.TryCreate(options.CloudPacsUrl, UriKind.Absolute, out var cloudPacsUrl)
            || string.IsNullOrWhiteSpace(options.CloudPacsApiKey))
        {
            throw new InvalidOperationException(
                "Agent:Vendor 'CloudPacs' needs Agent:CloudPacsUrl (absolute URL) and Agent:CloudPacsApiKey.");
        }

        var baseUrl = cloudPacsUrl.AbsoluteUri.EndsWith('/') ? cloudPacsUrl : new Uri(cloudPacsUrl.AbsoluteUri + "/");
        builder.Services.AddHttpClient(AgentOptions.CloudPacsVendor);
        builder.Services.AddSingleton<IPacsAdapter>(sp => new CloudPacsAdapter(
            new CloudPacsAdapterOptions(baseUrl, options.CloudPacsApiKey, options.WebhookListenUrl, options.WebhookPublicUrl),
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(AgentOptions.CloudPacsVendor),
            sp.GetRequiredService<ILogger<CloudPacsAdapter>>()));
        break;
    default:
        throw new InvalidOperationException(
            $"Unknown Agent:Vendor '{options.Vendor}'. Expected '{AgentOptions.LegacyPacsVendor}' or '{AgentOptions.CloudPacsVendor}'.");
}

builder.Services.AddSingleton<EnrollmentService>();
builder.Services.AddHostedService<AgentWorker>();

await builder.Build().RunAsync();
