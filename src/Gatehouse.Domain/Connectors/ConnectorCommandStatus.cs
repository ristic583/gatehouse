namespace Gatehouse.Domain.Connectors;

public enum ConnectorCommandStatus
{
    Queued,
    Delivered,
    Acked,
    Failed,
    Superseded,
}
