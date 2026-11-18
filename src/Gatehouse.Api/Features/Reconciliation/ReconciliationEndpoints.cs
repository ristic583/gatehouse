using Gatehouse.Api.Http;
using Gatehouse.Application.Authorization;
using Gatehouse.Application.Reconciliation.GetReconciliationReport;
using Gatehouse.Application.Reconciliation.ListReconciliationInstances;
using Gatehouse.Application.Reconciliation.ListReconciliationReports;
using Gatehouse.Application.Reconciliation.RequestReconciliation;
using Gatehouse.Domain.Fulfillment;

namespace Gatehouse.Api.Features.Reconciliation;

public static class ReconciliationEndpoints
{
    public static IEndpointRouteBuilder MapReconciliationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/reconciliation/reports", async (
            RequestReconciliationRequest request, RequestReconciliationHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new RequestReconciliationCommand(new PacsInstanceId(request.PacsInstanceId)), ct))
                    .ToHttpResult(dto => Results.Accepted($"/api/v1/reconciliation/reports/{dto.ReportId}", dto)))
            .RequireAuthorization(Policies.SecurityStaff);

        endpoints.MapGet("/api/v1/reconciliation/instances", async (ListReconciliationInstancesHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(ct)).ToHttpResult(Results.Ok))
            .RequireAuthorization(Policies.StaffConsole)
            .WithName("ListReconciliationInstances");

        endpoints.MapGet("/api/v1/reconciliation/reports", async (
            Guid? pacsInstanceId, int? limit, ListReconciliationReportsHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new ListReconciliationReportsQuery(pacsInstanceId, limit), ct)).ToHttpResult(Results.Ok))
            .RequireAuthorization(Policies.StaffConsole)
            .WithName("ListReconciliationReports");

        endpoints.MapGet("/api/v1/reconciliation/reports/{reportId:guid}", async (
            Guid reportId, GetReconciliationReportHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new GetReconciliationReportQuery(reportId), ct)).ToHttpResult(Results.Ok))
            .RequireAuthorization(Policies.StaffConsole)
            .WithName("GetReconciliationReport");
        return endpoints;
    }
}
