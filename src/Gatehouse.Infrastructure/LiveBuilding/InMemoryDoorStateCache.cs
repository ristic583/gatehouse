using System.Collections.Concurrent;
using Gatehouse.Application.LiveBuilding.Dtos;

namespace Gatehouse.Infrastructure.LiveBuilding;

/// <summary>Process-wide storage behind <see cref="InMemoryDoorStateStore"/> (keys as in Redis).</summary>
public sealed class InMemoryDoorStateCache
{
    internal ConcurrentDictionary<string, DoorStateDto> Entries { get; } = new(StringComparer.Ordinal);
}
