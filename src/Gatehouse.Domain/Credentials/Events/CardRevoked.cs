using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;

namespace Gatehouse.Domain.Credentials.Events;

public sealed record CardRevoked(CredentialId CredentialId, PersonId PersonId, string Reason, DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt);
