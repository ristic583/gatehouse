using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;

namespace Gatehouse.Domain.Credentials.Events;

public sealed record CardActivated(CredentialId CredentialId, PersonId PersonId, DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
