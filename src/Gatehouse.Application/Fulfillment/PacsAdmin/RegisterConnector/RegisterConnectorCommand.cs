namespace Gatehouse.Application.Fulfillment.PacsAdmin.RegisterConnector;

public sealed record RegisterConnectorCommand(Guid PacsInstanceId, string GatewayUrl);
