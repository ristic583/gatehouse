using Gatehouse.Application.Identity;
using Gatehouse.Domain.People;
using Microsoft.AspNetCore.Authorization;

namespace Gatehouse.Application.Authorization.Handlers;

/// <summary>Base for handlers that switch on requirement type and need the current person.</summary>
public abstract class ResourceHandler(ICurrentUser currentUser) : IAuthorizationHandler
{
    protected ICurrentUser CurrentUser { get; } = currentUser;

    public Task HandleAsync(AuthorizationHandlerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (CurrentUser.PersonId is not { } me)
        {
            return Task.CompletedTask;
        }

        foreach (var requirement in context.Requirements)
        {
            Handle(context, requirement, me);
        }

        return Task.CompletedTask;
    }

    protected abstract void Handle(AuthorizationHandlerContext context, IAuthorizationRequirement requirement, PersonId currentPersonId);
}
