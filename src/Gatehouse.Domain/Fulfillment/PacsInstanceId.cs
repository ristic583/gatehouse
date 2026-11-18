namespace Gatehouse.Domain.Fulfillment;

public readonly record struct PacsInstanceId(Guid Value)
{
    public static PacsInstanceId New() => new(Guid.CreateVersion7());
}
