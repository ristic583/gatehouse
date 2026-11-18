using Gatehouse.Domain.People;

namespace Gatehouse.Application.Access;

public sealed record PersonSnapshot(PersonId Id, PersonId? ManagerId, PersonId? SponsorId);
