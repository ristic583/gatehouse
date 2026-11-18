using Gatehouse.Application.Connectors;
using Gatehouse.Application.Reconciliation.CompareReconciliation;
using Gatehouse.Application.Reconciliation.FailReconciliation;
using Gatehouse.Application.Reconciliation.GetReconciliationReport;
using Gatehouse.Application.Reconciliation.ListReconciliationInstances;
using Gatehouse.Application.Reconciliation.ListReconciliationReports;
using Gatehouse.Application.Reconciliation.RequestReconciliation;
using Gatehouse.Application.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gatehouse.Application.Reconciliation;

public static class ReconciliationModule
{
    public static IServiceCollection AddReconciliationModule(this IServiceCollection services)
    {
        services.AddReconciliationWorkerHandlers();
        services.AddScoped<RequestReconciliationHandler>();
        services.AddScoped<ReconciliationReadModel>();
        services.AddScoped<ListReconciliationInstancesHandler>();
        services.AddScoped<ListReconciliationReportsHandler>();
        services.AddScoped<GetReconciliationReportHandler>();
        return services;
    }

    /// <summary>Handlers the Workers host needs (no current user or authorization service there).</summary>
    public static IServiceCollection AddReconciliationWorkerHandlers(this IServiceCollection services)
    {
        services.TryAddScoped<ConnectorCommandFactory>();
        services.TryAddScoped<ReconciliationStarter>();
        services.TryAddScoped<StaffTaskService>();
        services.TryAddScoped<CompareReconciliationHandler>();
        services.TryAddScoped<FailReconciliationHandler>();
        services.TryAddScoped<Remediation.ApplyFindingRemovalAckHandler>();
        return services;
    }
}
