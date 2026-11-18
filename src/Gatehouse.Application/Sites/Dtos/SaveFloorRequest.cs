namespace Gatehouse.Application.Sites.Dtos;

public sealed record SaveFloorRequest(string Name, int Level, int PlanWidth, int PlanHeight);
