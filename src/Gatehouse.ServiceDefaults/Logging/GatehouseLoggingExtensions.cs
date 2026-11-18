using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Formatting.Compact;

namespace Gatehouse.ServiceDefaults.Logging;

public static class GatehouseLoggingExtensions
{
    public static TBuilder AddGatehouseLogging<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        var isDevelopment = builder.Environment.IsDevelopment();
        var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];

        builder.Services.AddSerilog((services, configuration) =>
        {
            configuration
                .ReadFrom.Configuration(builder.Configuration)
                .ConfigureGatehouseDefaults();

            if (isDevelopment)
            {
                configuration.WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}",
                    formatProvider: System.Globalization.CultureInfo.InvariantCulture);
            }
            else
            {
                configuration.WriteTo.Console(new CompactJsonFormatter());
            }

            if (!string.IsNullOrWhiteSpace(otlpEndpoint))
            {
                configuration.WriteTo.OpenTelemetry(_ => { });
            }
        });

        return builder;
    }

    /// <summary>Redaction and enrichment shared by every host and by the log-redaction tests.</summary>
    public static LoggerConfiguration ConfigureGatehouseDefaults(this LoggerConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration
            .Destructure.With<SensitiveDataDestructuringPolicy>()
            .Enrich.FromLogContext();
    }
}
