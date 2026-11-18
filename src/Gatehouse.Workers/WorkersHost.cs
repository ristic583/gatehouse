using Gatehouse.Application.Access.Notifications;
using Gatehouse.Application.Audit;
using Gatehouse.Application.Common.Messaging;
using Gatehouse.Application.Connectors;
using Gatehouse.Application.Credentials;
using Gatehouse.Application.Fulfillment;
using Gatehouse.Application.LiveBuilding;
using Gatehouse.Application.People;
using Gatehouse.Application.Platform;
using Gatehouse.Application.Platform.Provisioning;
using Gatehouse.Application.Reconciliation;
using Gatehouse.Application.Reconciliation.Scheduling;
using Gatehouse.Application.Tasks;
using Gatehouse.Infrastructure;
using Gatehouse.Infrastructure.LiveBuilding;
using Gatehouse.Infrastructure.Messaging;
using Gatehouse.Infrastructure.Platform;
using Gatehouse.Infrastructure.Tenancy;

namespace Gatehouse.Workers;

/// <summary>Builds the Workers host; <c>Program</c> runs it and <c>WorkersHostTests</c> validates its services.</summary>
public static class WorkersHost
{
    public static HostApplicationBuilder CreateBuilder(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.AddServiceDefaults();
        if (!string.IsNullOrEmpty(builder.Configuration.GetConnectionString("redis")))
        {
            builder.AddRedisClient("redis");
        }

        builder.AddGatehouseCatalog();
        builder.AddGatehouseTenancy();
        builder.Services.AddTenantStatusChannel();
        builder.Services.AddAuditModule();
        builder.Services.AddGatehousePlatform();
        builder.Services.AddOptions<ProvisioningOptions>().BindConfiguration(ProvisioningOptions.SectionName);
        builder.Services.AddOptions<TenantStatisticsOptions>().BindConfiguration(TenantStatisticsOptions.SectionName);
        builder.Services.AddTaskFollowUps();
        builder.Services.AddConnectorsModule();
        builder.Services.AddReconciliationWorkerHandlers();
        builder.Services.AddCredentialsWorkerHandlers();
        builder.Services.AddPeopleWorkerHandlers();
        builder.Services.AddOptions<NotificationOptions>().BindConfiguration(NotificationOptions.Section);
        builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();
        builder.Services.AddScoped<NotifyApproversHandler>();
        builder.Services.AddOptions<ReconciliationScheduleOptions>().BindConfiguration(ReconciliationScheduleOptions.SectionName);
        builder.Services.AddSingleton<ReconciliationScheduleRunner>();
        builder.Services.AddLiveBuildingInfrastructure();
        builder.Services.AddLiveBuildingWorkerHandlers();
        builder.Services.AddScoped<DoorEventPartitionMaintenance>();
        builder.Services.AddRevocationMonitoring();
        builder.Services.AddGatehouseWorkersMessaging(builder.Configuration);
        builder.Services.AddHostedService<OutboxRelayWorker>();
        builder.Services.AddHostedService<HeartbeatWorker>();
        builder.Services.AddHostedService<RevocationMonitorWorker>();
        builder.Services.AddHostedService<DoorEventPartitionMaintenanceJob>();
        builder.Services.AddHostedService<ConnectorOfflineSweeper>();
        builder.Services.AddHostedService<ReconciliationSchedulerJob>();
        builder.Services.AddHostedService<LifecycleJob>();
        builder.Services.AddHostedService<HrSyncJob>();
        builder.Services.AddHostedService<RetentionJob>();
        builder.Services.AddHostedService<TenantProvisioningJob>();
        builder.Services.AddHostedService<TenantStatisticsJob>();

        return builder;
    }
}
