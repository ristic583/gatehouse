namespace Gatehouse.Domain.Platform;

public sealed record PlatformActor(PlatformActorKind Kind, string Id)
{
    public static PlatformActor System { get; } = new(PlatformActorKind.System, "platform");
}
