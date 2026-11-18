using Gatehouse.Api.Http;
using Gatehouse.Application.Authorization;
using Gatehouse.Application.People.Admin.AssignRole;
using Gatehouse.Application.People.Admin.CreateEmployee;
using Gatehouse.Application.People.Admin.ImportPeople;
using Gatehouse.Application.People.Admin.ListRoleAssignments;
using Gatehouse.Application.People.Admin.RemoveRoleAssignment;
using Gatehouse.Application.People.Dtos;
using Gatehouse.Application.People.Termination;
using Gatehouse.Domain.People;

namespace Gatehouse.Api.Features.People;

/// <summary>Tenant admin: employees, CSV import and role assignments (authorization.md §11).</summary>
public static class PeopleAdminEndpoints
{
    /// <summary>1 MiB CSV plus multipart overhead.</summary>
    public const long ImportLimitBytes = ImportPeopleHandler.MaxImportBytes + (64 * 1024);

    public static IEndpointRouteBuilder MapPeopleAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/v1/admin").RequireAuthorization(Policies.TenantAdmin);

        admin.MapPost("/people/employees", async (CreateEmployeeRequest request, CreateEmployeeHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new CreateEmployeeCommand(request), ct))
                    .ToHttpResult(dto => Results.Created($"/api/v1/people/{dto.PersonId}", dto)))
            .WithName("CreateEmployee");

        admin.MapPost("/people/import", async (HttpRequest request, bool? dryRun, ImportPeopleHandler handler, CancellationToken ct) =>
            {
                var file = await ReadUploadAsync(request, ct);
                var result = file is null
                    ? ImportPeopleHandler.TooLarge
                    : await handler.HandleAsync(new ImportPeopleCommand(file, dryRun ?? false), ct);
                if (result.IsFailure && result.Error.Code == "people_import.too_large")
                {
                    return Results.Problem(statusCode: StatusCodes.Status413PayloadTooLarge, title: result.Error.Message,
                        extensions: new Dictionary<string, object?> { ["code"] = result.Error.Code });
                }

                return result.ToHttpResult(Results.Ok);
            })
            .DisableAntiforgery()
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(ImportLimitBytes))
            .WithName("ImportPeople");

        admin.MapPost("/people/{personId:guid}/terminate", async (Guid personId, TerminatePersonHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new TerminatePersonCommand(new PersonId(personId)), ct)).ToHttpResult(Results.Ok))
            .WithName("TerminatePerson");

        admin.MapGet("/role-assignments", async (ListRoleAssignmentsHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new ListRoleAssignmentsQuery(), ct)).ToHttpResult(Results.Ok))
            .WithName("ListRoleAssignments");
        admin.MapPost("/role-assignments", async (AssignRoleRequest request, AssignRoleHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(new AssignRoleCommand(request), ct))
                .ToHttpResult(dto => Results.Created($"/api/v1/admin/role-assignments/{dto.Id}", dto)));
        admin.MapDelete("/role-assignments/{roleAssignmentId:guid}",
            async (Guid roleAssignmentId, RemoveRoleAssignmentHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new RemoveRoleAssignmentCommand(roleAssignmentId), ct)).ToHttpResult());

        return endpoints;
    }

    /// <summary>The multipart <c>file</c> bytes; empty when missing (reported as an invalid file), null when over 1 MiB.</summary>
    private static async Task<byte[]?> ReadUploadAsync(HttpRequest request, CancellationToken ct)
    {
        if (!request.HasFormContentType)
        {
            return [];
        }

        var form = await request.ReadFormAsync(ct);
        var file = form.Files.GetFile("file");
        if (file is null)
        {
            return [];
        }

        if (file.Length > ImportPeopleHandler.MaxImportBytes)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }
}
