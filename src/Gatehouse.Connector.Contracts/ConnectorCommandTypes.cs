namespace Gatehouse.Connector.Contracts;

public static class ConnectorCommandTypes
{
    public const string UpsertCardholder = "UpsertCardholder";
    public const string GrantAccess = "GrantAccess";
    public const string RevokeAccess = "RevokeAccess";
    public const string ActivateCard = "ActivateCard";
    public const string SuspendCard = "SuspendCard";
    public const string SetPin = "SetPin";
    public const string MomentaryUnlock = "MomentaryUnlock";
    public const string Lockdown = "Lockdown";
    public const string ExportRecords = "ExportRecords";
}
