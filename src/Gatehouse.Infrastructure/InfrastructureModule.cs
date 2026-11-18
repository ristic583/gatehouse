using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Connectors;
using Gatehouse.Application.Credentials;
using Gatehouse.Application.Credentials.Photos;
using Gatehouse.Application.Fulfillment;
using Gatehouse.Application.Identity.MagicLinks;
using Gatehouse.Application.People.HrSync;
using Gatehouse.Application.Tenancy;
using Gatehouse.Infrastructure.Fulfillment;
using Gatehouse.Infrastructure.Identity;
using Gatehouse.Infrastructure.Messaging.Outbox;
using Gatehouse.Infrastructure.People;
using Gatehouse.Infrastructure.Persistence;
using Gatehouse.Infrastructure.Persistence.Catalog;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Gatehouse.Infrastructure.Security;
using Gatehouse.Infrastructure.Seeding;
using Gatehouse.Infrastructure.Storage;
using Gatehouse.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gatehouse.Infrastructure;

public static class InfrastructureModule
{
    public static IHostApplicationBuilder AddGatehouseCatalog(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddDbContext<CatalogDbContext>((sp, options) => options
            .UseNpgsql(RequiredConnectionString(sp, "catalog"))
            .UseSnakeCaseNamingConvention());
        builder.Services.AddSingleton<ISecretProtector, AesGcmSecretProtector>();
        builder.Services.AddScoped<ICatalogReader, CatalogReader>();
        builder.Services.AddSingleton<IHrFeedSource, FileHrFeedSource>();
        builder.Services.AddSingleton<IPinVault, LocalPinVault>();
        builder.Services.AddSingleton<IPinTransportEncryptor, PinTransportEncryptor>();
        builder.Services.AddSingleton<ICardNumberHasher, CardNumberHasher>();
        builder.Services.AddSingleton<IMagicLinkTokenCodec, MagicLinkTokenCodec>();
        builder.Services.AddSingleton<IPhotoProcessor, PhotoProcessor>();
        if (string.Equals(builder.Configuration["Storage:Provider"], "memory", StringComparison.OrdinalIgnoreCase))
        {
            builder.Services.AddSingleton<IObjectStore, InMemoryObjectStore>();
        }
        else
        {
            builder.Services.AddSingleton<IObjectStore, S3ObjectStore>();
        }

        builder.Services.AddOptions<Identity.Keycloak.KeycloakOptions>().BindConfiguration(Identity.Keycloak.KeycloakOptions.Section);
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<Identity.Keycloak.KeycloakAdminTokenCache>();
        builder.Services.AddHttpClient<IIdentityProvisioner, Identity.Keycloak.KeycloakIdentityProvisioner>(
            c => c.Timeout = TimeSpan.FromSeconds(15));

        builder.Services.AddSingleton<GuestTokenIssuer>();
        builder.Services.AddSingleton<IGuestTokenIssuer>(sp => sp.GetRequiredService<GuestTokenIssuer>());

        return builder;
    }

    public static IHostApplicationBuilder AddGatehouseTenancy(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddMemoryCache();
        builder.Services.AddScoped<TenantContext>();
        builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        builder.Services.AddScoped<ITenantContextSetter>(sp => sp.GetRequiredService<TenantContext>());
        builder.Services.AddSingleton<DomainEventOutboxInterceptor>();
        builder.Services.AddSingleton<Messaging.IOutboxSignal>(sp =>
            sp.GetService<StackExchange.Redis.IConnectionMultiplexer>() is { } mux
                ? new Messaging.RedisOutboxSignal(mux, sp.GetRequiredService<ILogger<Messaging.RedisOutboxSignal>>())
                : new Messaging.PollingOutboxSignal());
        builder.Services.AddScoped<OutboxSignalInterceptor>();
        builder.Services.AddDbContext<TenantDbContext>((sp, options) => options
            .UseNpgsql(sp.GetRequiredService<TenantContext>().ConnectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(sp.GetRequiredService<DomainEventOutboxInterceptor>(), sp.GetRequiredService<OutboxSignalInterceptor>()));
        builder.Services.AddScoped<ITenantDb>(sp => sp.GetRequiredService<TenantDbContext>());
        builder.Services.AddScoped<ITenantConnectionResolver, TenantConnectionResolver>();
        builder.Services.AddSingleton<ITenantScopeFactory, TenantScopeFactory>();
        builder.Services.AddSingleton<ITenantLoop>(new TenantLoop(
            builder.Configuration.GetValue(TenantLoop.MaxConcurrentTenantsSetting, TenantLoop.DefaultMaxConcurrentTenants)));
        builder.Services.AddSingleton<TenantDatabaseProvisioner>();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<MigrationRunner>();
        builder.Services.AddScoped<TenantProvisioningService>();
        builder.Services.AddSingleton<DemoSeeder>();
        builder.Services.AddScoped<DemoOrganizationSeeder>();
        builder.Services.AddSingleton<Messaging.OutboxMessageTypeRegistry>();
        builder.Services.AddSingleton<Messaging.OutboxRelay>();
        builder.Services.AddSingleton(sp => new Messaging.OutboxRelayScheduler(
            sp.GetRequiredService<Messaging.IOutboxSignal>(),
            sp.GetRequiredService<TimeProvider>(),
            TimeSpan.FromMilliseconds(builder.Configuration.GetValue(
                Messaging.OutboxRelayScheduler.SweepIntervalSetting, Messaging.OutboxRelayScheduler.DefaultSweepIntervalMs))));
        builder.Services.AddScoped<Application.Common.Messaging.IMessageDeduplicator, Messaging.MessageDeduplicator>();
        builder.Services.AddScoped<Application.Common.Messaging.IOutboxWriter, Messaging.OutboxWriter>();
        builder.Services.AddScoped<Application.Connectors.IConnectorSequencer, Connectors.ConnectorSequencer>();
        builder.Services.AddScoped<Connectors.ConnectorCertificateAuthority>();
        builder.Services.AddScoped<Connectors.ConnectorRegistrationService>();
        builder.Services.AddSingleton<Application.Connectors.IConnectorPollGate>(sp =>
            sp.GetService<StackExchange.Redis.IConnectionMultiplexer>() is { } mux
                ? new Connectors.RedisConnectorPollGate(mux)
                : new Application.Connectors.InMemoryConnectorPollGate());
        builder.Services.AddSingleton<Application.Connectors.IConnectorCommandSignal>(sp =>
            sp.GetService<StackExchange.Redis.IConnectionMultiplexer>() is { } mux
                ? new Connectors.RedisConnectorCommandSignal(mux)
                : new Application.Connectors.InMemoryConnectorCommandSignal());
        builder.Services.AddSingleton<Application.Connectors.IDoorEventDeduplicator>(sp =>
            sp.GetService<StackExchange.Redis.IConnectionMultiplexer>() is { } mux
                ? new Connectors.RedisDoorEventDeduplicator(mux)
                : new Application.Connectors.InMemoryDoorEventDeduplicator(sp.GetRequiredService<TimeProvider>()));
        builder.Services.TryAddScoped<IConnectorHealthReader, PacsInstanceConnectorHealthReader>();

        return builder;
    }

    private static string RequiredConnectionString(IServiceProvider services, string name) =>
        services.GetRequiredService<IConfiguration>().GetConnectionString(name)
            ?? throw new InvalidOperationException($"Connection string '{name}' is not configured.");
}
