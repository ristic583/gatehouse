using Gatehouse.Domain.People;

namespace Gatehouse.Application.People.Sponsorship;

public sealed record ExtendSponsorshipCommand(PersonId PersonId, DateTimeOffset End);
