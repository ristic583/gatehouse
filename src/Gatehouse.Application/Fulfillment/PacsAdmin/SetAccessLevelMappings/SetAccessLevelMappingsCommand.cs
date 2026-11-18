namespace Gatehouse.Application.Fulfillment.PacsAdmin.SetAccessLevelMappings;

public sealed record SetAccessLevelMappingsCommand(Guid PacsInstanceId, IReadOnlyList<AccessLevelMappingRequest> Mappings);
