using Gatehouse.Application.Access.Dtos;

namespace Gatehouse.Application.Sites.Dtos;

/// <summary>Create or update a door; <paramref name="AreaId"/> on update moves the door to another area of the same floor.</summary>
public sealed record SaveDoorRequest(string Name, PointDto Position, string? PacsDoorRef, Guid? AreaId = null);
