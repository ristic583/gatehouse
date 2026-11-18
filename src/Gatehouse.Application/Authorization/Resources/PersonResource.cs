using Gatehouse.Domain.People;

namespace Gatehouse.Application.Authorization.Resources;

public sealed record PersonResource(PersonId PersonId, PersonType Type, PersonId? ManagerId, PersonId? SponsorId);
