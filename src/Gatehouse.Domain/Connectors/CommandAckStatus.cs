namespace Gatehouse.Domain.Connectors;

/// <summary>Domain mirror of Connector.Contracts AckStatus (Domain cannot reference Contracts).</summary>
public enum CommandAckStatus
{
    Applied,
    Failed,
    Unsupported,
}
