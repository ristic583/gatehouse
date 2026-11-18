namespace Gatehouse.Application.Fulfillment.PacsAdmin.UpdatePacsInstance;

public sealed record UpdatePacsInstanceCommand(Guid PacsInstanceId, SavePacsInstanceRequest Request);
