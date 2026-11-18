using Gatehouse.Domain.People;

namespace Gatehouse.Application.People.Termination;

public sealed record TerminatePersonCommand(PersonId PersonId);
