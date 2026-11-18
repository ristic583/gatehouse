namespace Gatehouse.Application.Sites.Dtos;

public sealed record SecurityLevelDto(Guid SecurityLevelId, string Name, string ApproverChain, int MaxDurationDays);
