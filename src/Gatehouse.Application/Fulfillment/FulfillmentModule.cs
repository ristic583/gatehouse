using FluentValidation;
using Gatehouse.Application.Audit;
using Gatehouse.Application.Fulfillment.Dtos;
using Gatehouse.Application.Fulfillment.FulfillmentActions;
using Gatehouse.Application.Fulfillment.GetFulfillment;
using Gatehouse.Application.Fulfillment.ListFulfillments;
using Gatehouse.Application.Fulfillment.MonitorRevocations;
using Gatehouse.Application.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gatehouse.Application.Fulfillment;

public static class FulfillmentModule
{
    public static IServiceCollection AddFulfillmentModule(this IServiceCollection services)
    {
        services.AddScoped<FulfillmentDtoBuilder>();
        services.AddScoped<ListFulfillmentsHandler>();
        services.AddScoped<GetFulfillmentHandler>();
        services.AddScoped<FulfillmentDetailLoader>();
        services.AddScoped<FulfillmentActionService>();
        services.AddScoped<FulfillmentPlanner>();
        services.TryAddScoped<PacsAdmin.GrantMigrationService>();
        services.AddScoped<StartFulfillmentHandler>();
        services.AddScoped<ConfirmFulfillmentHandler>();
        services.AddScoped<FailFulfillmentHandler>();
        services.TryAddSingleton<IValidator<FailFulfillmentDto>, FailFulfillmentValidator>();
        services.AddScoped<RetryFulfillmentHandler>();
        services.AddRevocationMonitoring();
        return services;
    }

    public static IServiceCollection AddRevocationMonitoring(this IServiceCollection services)
    {
        services.AddOptions<RevocationMonitorOptions>().BindConfiguration(RevocationMonitorOptions.SectionName);
        services.TryAddScoped<StaffTaskService>();
        services.TryAddScoped<IAuditLog, AuditLog>();
        services.TryAddScoped<DetectUnconfirmedRevocationsHandler>();
        services.TryAddSingleton<RevocationMonitorRunner>();
        return services;
    }
}
