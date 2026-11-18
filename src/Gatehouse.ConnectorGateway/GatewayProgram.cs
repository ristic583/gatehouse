using System.Threading.RateLimiting;
using Gatehouse.Application.Audit;
using Gatehouse.Application.Connectors;
using Gatehouse.Connector.Contracts;
using Gatehouse.Infrastructure;
using Gatehouse.Infrastructure.Connectors;
using Gatehouse.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Gatehouse.ConnectorGateway;

public sealed class GatewayProgram
{
    public const long MaxBodyBytes = 5 * 1024 * 1024;

    /// <summary>Requests an anonymous client (one remote IP) may send at once before the 10-per-minute refill applies.</summary>
    public const int AnonymousBurst = 20;

    public static Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();
        if (!string.IsNullOrEmpty(builder.Configuration.GetConnectionString("redis")))
        {
            builder.AddRedisClient("redis");
        }

        builder.AddGatehouseCatalog();
        builder.AddGatehouseTenancy();
        builder.Services.AddTenantStatusChannel();
        builder.Services.AddAuditModule();
        builder.Services.AddConnectorsModule();
        builder.Services.AddProblemDetails();
        builder.Services.AddScoped<EnrollConnectorService>();
        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.PropertyNameCaseInsensitive = true;
            foreach (var c in ConnectorJson.Options.Converters)
            {
                o.SerializerOptions.Converters.Add(c);
            }
        });

        builder.WebHost.ConfigureKestrel(k =>
        {
            k.Limits.MaxRequestBodySize = MaxBodyBytes;
            k.ConfigureHttpsDefaults(h =>
            {
                h.ClientCertificateMode = ClientCertificateMode.AllowCertificate;
                h.AllowAnyClientCertificate();
            });
        });

        builder.Services.AddAuthentication(ConnectorCertificateAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, ConnectorCertificateAuthenticationHandler>(
                ConnectorCertificateAuthenticationHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(o =>
        {
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
            {
                var key = http.User.FindFirst("connector_id")?.Value;
                return key is null
                    // SEC-06/PT-30: anonymous callers (/enroll, failed client certificates) are limited per remote IP before any
                    // catalog lookup: a burst of 20, then 10 per minute.
                    ? RateLimitPartition.GetTokenBucketLimiter(
                        "anonymous:" + http.Connection.RemoteIpAddress, _ => new TokenBucketRateLimiterOptions
                        {
                            TokenLimit = AnonymousBurst,
                            TokensPerPeriod = 10,
                            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                            QueueLimit = 0,
                            AutoReplenishment = true,
                        })
                    : RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = 50,
                        TokensPerPeriod = 10,
                        ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    });
            });
            o.OnRejected = async (context, ct) =>
            {
                var retry = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var after)
                    ? (int)Math.Ceiling(after.TotalSeconds)
                    : 1;
                context.HttpContext.Response.Headers.RetryAfter = Math.Max(retry, 1)
                    .ToString(System.Globalization.CultureInfo.InvariantCulture);
                await ConnectorProblems.Create(context.HttpContext, "connector.rate_limited", 429, "Rate limited", "Too many requests.")
                    .ExecuteAsync(context.HttpContext);
            };
        });

        var app = builder.Build();
        app.UseExceptionHandler();
        app.Use(async (http, next) =>
        {
            var limit = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (limit is { IsReadOnly: false })
            {
                limit.MaxRequestBodySize = MaxBodyBytes;
            }

            if (http.Request.ContentLength > MaxBodyBytes)
            {
                await ConnectorProblems.Create(http, "connector.payload_too_large", 413, "Payload too large", "Body exceeds 5 MiB.")
                    .ExecuteAsync(http);
                return;
            }

            try
            {
                await next(http);
            }
            catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge && !http.Response.HasStarted)
            {
                await ConnectorProblems.Create(http, "connector.payload_too_large", 413, "Payload too large", "Body exceeds 5 MiB.")
                    .ExecuteAsync(http);
            }
        });
        app.UseAuthentication();
        // Before authorization, so that callers refused for a bad client certificate are limited too (PT-30).
        app.UseRateLimiter();
        app.UseAuthorization();
        app.MapDefaultEndpoints();
        app.MapConnectorEndpoints();
        return app.RunAsync();
    }
}
