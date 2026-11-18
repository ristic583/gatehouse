using Gatehouse.Domain.Access;
using Gatehouse.Domain.Credentials;
using Gatehouse.Domain.Fulfillment;

namespace Gatehouse.Application.People.Termination;

public sealed record TerminationOutcome(
    IReadOnlyList<EntitlementId> RevokedEntitlementIds, IReadOnlyList<CredentialId> RevokedCardIds,
    IReadOnlyList<CommandId> CardCommandIds);
