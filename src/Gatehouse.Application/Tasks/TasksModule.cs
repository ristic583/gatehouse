using Gatehouse.Application.Connectors;
using Gatehouse.Application.Reconciliation.Remediation;
using Gatehouse.Application.Tasks.ListStaffTasks;
using Gatehouse.Application.Tasks.StaffTaskActions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gatehouse.Application.Tasks;

public static class TasksModule
{
    public static IServiceCollection AddTasksModule(this IServiceCollection services)
    {
        services.AddTaskFollowUps();
        services.AddScoped<StaffTaskDtoBuilder>();
        services.AddScoped<ListStaffTasksHandler>();
        services.AddScoped<StaffTaskActionService>();
        services.AddScoped<ClaimStaffTaskHandler>();
        services.AddScoped<ResolveStaffTaskHandler>();
        services.TryAddScoped<ConnectorCommandFactory>();
        return services;
    }

    /// <summary>Handlers used by message consumers (no current user); the Workers host registers only these.</summary>
    public static IServiceCollection AddTaskFollowUps(this IServiceCollection services)
    {
        services.AddScoped<StaffTaskService>();
        services.AddScoped<FulfillmentFollowUps.OpenFulfillmentFailedTaskHandler>();
        services.AddScoped<FulfillmentFollowUps.ResolveFulfillmentTasksHandler>();
        return services;
    }
}
