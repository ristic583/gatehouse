using Gatehouse.Domain.Credentials;

namespace Gatehouse.Application.Credentials.DeactivateCard;

public sealed record DeactivateCardCommand(CredentialId CardId, string Reason);
