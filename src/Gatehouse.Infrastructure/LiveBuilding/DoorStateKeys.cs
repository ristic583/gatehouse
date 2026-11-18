using Gatehouse.Domain.Sites;

namespace Gatehouse.Infrastructure.LiveBuilding;

internal static class DoorStateKeys
{
    public static string For(string tenantId, DoorId doorId) => $"tenant:{tenantId}:door:{doorId.Value}:state";

    public static string LiveChannel(string tenantId) => $"tenant:{tenantId}:live";
}
