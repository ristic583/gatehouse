namespace Gatehouse.Domain.Fulfillment;

public readonly record struct AccessLevelMappingId(Guid Value)
{
    public static AccessLevelMappingId New() => new(Guid.CreateVersion7());
}
