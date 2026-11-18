using Gatehouse.Domain.Fulfillment;

namespace Gatehouse.Application.Credentials.CardCommands;

public sealed record CardCommandTarget(PacsInstanceId PacsInstanceId, string? CapabilitiesJson);
