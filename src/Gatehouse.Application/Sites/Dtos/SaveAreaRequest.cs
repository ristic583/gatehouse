using Gatehouse.Application.Access.Dtos;

namespace Gatehouse.Application.Sites.Dtos;

/// <summary><c>OwnerIds</c> are required on create; <c>null</c> on update keeps the owners (use <c>PUT …/owners</c>).</summary>
public sealed record SaveAreaRequest(
    string Name,
    string Code,
    IReadOnlyList<PointDto> Polygon,
    Guid SecurityLevelId,
    IReadOnlyList<Guid>? OwnerIds,
    bool? AccessControlled = null);
