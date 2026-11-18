namespace Gatehouse.Application.LiveBuilding.RequestDoorCommand;

public sealed record DoorCommandResultDto(Guid DoorCommandId, int DoorCount, int SkippedDoorCount);
