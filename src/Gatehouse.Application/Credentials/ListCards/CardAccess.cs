using System.Security.Claims;
using Gatehouse.Application.Authorization;
using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Application.Authorization.Resources;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Identity;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Credentials.ListCards;

/// <summary>Who may see and handle a person's cards: self, <see cref="ManagePersonRequirement"/> or security staff who see the person
/// (authorization.md §4).</summary>
public sealed class CardAccess(ITenantDb db, ICurrentUser currentUser, IAuthorizationService authorization)
{
    public static Error Terminated() => Error.Conflict("person.terminated", "The person is already terminated.");

    public static Error Forbidden() => Error.Forbidden("auth.forbidden", "You are not allowed to handle cards of this person.");

    public static PersonResource ResourceFor(Person person)
    {
        ArgumentNullException.ThrowIfNull(person);
        return new PersonResource(person.Id, person.Type, person.ManagerId, person.SponsorId);
    }

    public Task<Person?> FindPersonAsync(PersonId personId, CancellationToken ct) =>
        db.Persons.AsNoTracking().FirstOrDefaultAsync(p => p.Id == personId, ct);

    public async Task<bool> CanSeeAsync(PersonResource person)
    {
        ArgumentNullException.ThrowIfNull(person);
        return person.PersonId == currentUser.PersonId
            || await AllowedAsync(person, new ManagePersonRequirement())
            || (
                currentUser.IsSecurityStaff
                    && await db.Persons.AsNoTracking().VisibleTo(currentUser).AnyAsync(p => p.Id == person.PersonId));
    }

    public async Task<bool> AllowedAsync(PersonResource person, IAuthorizationRequirement requirement) =>
        (await authorization.AuthorizeAsync(new ClaimsPrincipal(), person, requirement)).Succeeded;

    /// <summary>Issue a card: who may manage the person, or security staff (the security desk issues badges; lab finding #6).</summary>
    public async Task<bool> CanIssueAsync(PersonResource person) =>
        currentUser.IsSecurityStaff || await AllowedAsync(person, new ManagePersonRequirement());

    /// <summary>Block a card: who may activate or manage it, or security staff (a reported-lost card; lab finding #5).</summary>
    public async Task<bool> CanDeactivateAsync(PersonResource person) =>
        currentUser.IsSecurityStaff || await AllowedAsync(person, new ActivateCardRequirement())
            || await AllowedAsync(person, new ManagePersonRequirement());
}
