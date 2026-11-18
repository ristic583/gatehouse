using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.People.Dtos;
using Gatehouse.Application.People.ListPeople;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.People.Admin.CreateEmployee;

/// <summary>
/// <c>POST /api/v1/admin/people/employees</c> (<c>domain-model.md</c> §2.10): authoritative, open-ended, email unique;
/// audit <c>person.created</c>.
/// </summary>
public sealed class CreateEmployeeHandler(ITenantDb db, PeopleAdminAudit audit, PersonSummaries summaries, TimeProvider time)
{
    public async Task<Result<PersonSummaryDto>> HandleAsync(CreateEmployeeCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var request = command.Request;
        ArgumentNullException.ThrowIfNull(request);
        var email = request.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        if (await db.Persons.AnyAsync(p => p.Email == email, ct))
        {
            return Error.Conflict("person.email_taken", "A person with this email already exists.");
        }

        PersonId? managerId = null;
        if (request.ManagerId is { } m)
        {
            var manager = new PersonId(m);
            if (!await db.Persons.AnyAsync(
                p => p.Id == manager && p.Type == PersonType.Employee && p.Status == PersonStatus.Active, ct))
            {
                return Error.NotFound("person.not_found", "The manager does not exist or is not an active employee.");
            }

            managerId = manager;
        }

        var now = time.GetUtcNow();
        var zone = string.IsNullOrWhiteSpace(request.TimeZoneId)
            ? await PeopleAdminDefaults.TimeZoneAsync(db, ct)
            : request.TimeZoneId.Trim();
        var eligibility = Eligibility.Create(now, null, zone);
        if (eligibility.IsFailure)
        {
            return eligibility.Error;
        }

        var person = Person.Create(PersonSource.Authoritative, PersonType.Employee, request.GivenName ?? string.Empty,
            request.FamilyName ?? string.Empty, email, eligibility.Value, managerId, null, now);
        if (person.IsFailure)
        {
            return person.Error;
        }

        db.Persons.Add(person.Value);
        var payload = new { type = "Employee", source = "Authoritative" };
        await audit.RecordAsync("person.created", "Person", person.Value.Id.Value.ToString(), payload, ct);
        await db.SaveChangesAsync(ct);
        return (await summaries.ToDtosAsync([person.Value], ct))[0];
    }
}
