using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;

namespace Gatehouse.Domain.Credentials.Events;

public sealed record CardActivationRequested(
    CredentialId CredentialId, PersonId PersonId, PersonId RequestedById, DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt);
