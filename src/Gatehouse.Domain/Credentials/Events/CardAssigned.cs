using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;

namespace Gatehouse.Domain.Credentials.Events;

public sealed record CardAssigned(CredentialId CredentialId, PersonId PersonId, string CardNumberMasked, DateTimeOffset OccurredAt)
    : DomainEvent(OccurredAt);
