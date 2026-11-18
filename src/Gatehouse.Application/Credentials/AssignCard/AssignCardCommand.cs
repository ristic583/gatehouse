using Gatehouse.Domain.People;

namespace Gatehouse.Application.Credentials.AssignCard;

public sealed record AssignCardCommand(PersonId PersonId, string CardNumber);
