using Gatehouse.Domain.Sites;

namespace Gatehouse.Application.Sites.Dtos;

/// <summary>
/// The plan's admin <c>SecurityLevelDto</c>; renamed because <see cref="SecurityLevelDto"/> already exists with other fields.
/// </summary>
public sealed record SecurityLevelAdminDto(Guid Id, string Name, int Rank, ApproverChain ApproverChain, int MaxDurationDays);
