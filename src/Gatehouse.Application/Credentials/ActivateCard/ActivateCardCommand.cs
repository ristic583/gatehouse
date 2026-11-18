using Gatehouse.Domain.Credentials;

namespace Gatehouse.Application.Credentials.ActivateCard;

public sealed record ActivateCardCommand(CredentialId CardId);
