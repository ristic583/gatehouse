using FluentValidation;
using Gatehouse.Api.Http;
using Gatehouse.Application.Authorization;
using Gatehouse.Application.People.Dtos;
using Gatehouse.Application.People.GetPerson;
using Gatehouse.Application.People.ListPeople;
using Gatehouse.Application.People.OnboardPerson;
using Gatehouse.Application.People.Sponsorship;
using Gatehouse.Domain.People;

namespace Gatehouse.Api.Features.People;

public static class PeopleEndpoints
{
    public static IEndpointRouteBuilder MapPeopleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/people",
            async (string? query, string? type, string? status, int? limit, ListPeopleHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new ListPeopleQuery(query, type, status, limit), ct)).ToHttpResult(Results.Ok))
            .RequireAuthorization(Policies.LinkedPerson)
            .WithName("ListPeople");

        endpoints.MapGet("/api/v1/people/{personId:guid}", async (Guid personId, GetPersonHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new GetPersonQuery(new PersonId(personId)), ct)).ToHttpResult(Results.Ok))
            .RequireAuthorization(Policies.LinkedPerson)
            .WithName("GetPerson");

        endpoints.MapPost("/api/v1/people", OnboardAsync).RequireAuthorization(Policies.LinkedPerson);

        endpoints.MapPut("/api/v1/people/{personId:guid}/eligibility",
            async (Guid personId, ExtendSponsorshipRequest request, ExtendSponsorshipHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new ExtendSponsorshipCommand(new PersonId(personId), request.End), ct)).ToHttpResult(Results.Ok))
            .RequireAuthorization(Policies.LinkedPerson);

        endpoints.MapPost("/api/v1/people/{personId:guid}/sponsorship/end",
            async (Guid personId, EndSponsorshipHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new EndSponsorshipCommand(new PersonId(personId)), ct)).ToHttpResult(Results.Ok))
            .RequireAuthorization(Policies.LinkedPerson);
        return endpoints;
    }

    private static async Task<IResult> OnboardAsync(
        OnboardPersonDto request,
        IValidator<OnboardPersonCommand> validator,
        OnboardPersonHandler handler,
        CancellationToken ct)
    {
        var command = new OnboardPersonCommand(request);
        var validation = await validator.ValidateAsync(command, ct);
        if (!validation.IsValid)
        {
            return validation.ToDictionary().ToValidationProblem();
        }

        return (await handler.HandleAsync(command, ct)).ToHttpResult(dto => Results.Created($"/api/v1/people/{dto.Summary.PersonId}", dto));
    }
}
