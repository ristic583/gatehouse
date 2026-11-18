using Gatehouse.Api.Http;
using Gatehouse.Application.Audit.ListAuditEntries;
using Gatehouse.Application.Audit.VerifyAuditChain;
using Gatehouse.Application.Authorization;

namespace Gatehouse.Api.Features.Audit;

public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/audit/verify", async (VerifyAuditChainHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new VerifyAuditChainQuery(), ct)).ToHttpResult(Results.Ok))
            .RequireAuthorization(Policies.AuditReader)
            .WithName("VerifyAuditChain");

        endpoints.MapGet("/api/v1/audit/entries", async (
            string? subjectType, string? subjectId, long? before, int? limit, ListAuditEntriesHandler handler, CancellationToken ct) =>
                (
                    await handler.HandleAsync(new ListAuditEntriesQuery(subjectType, subjectId, before, limit ?? 50), ct)).ToHttpResult(
                        Results.Ok))
            .RequireAuthorization(Policies.AuditReader)
            .WithName("ListAuditEntries");

        return endpoints;
    }
}
