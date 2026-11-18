namespace Gatehouse.Domain.Connectors;

public readonly record struct ConnectorId(Guid Value)
{
    public static ConnectorId New() => new(Guid.CreateVersion7());
}
