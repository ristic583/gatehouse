namespace Gatehouse.Agent;

public sealed record AgentOptions(
    string GatewayUrl,
    string DataDirectory,
    string? EnrollmentToken,
    string AgentVersion,
    string LegacyPacsConnectionString,
    bool TrustDevGatewayCertificate = false,
    string Vendor = AgentOptions.LegacyPacsVendor,
    string? CloudPacsUrl = null,
    string? CloudPacsApiKey = null,
    string WebhookListenUrl = AgentOptions.DefaultWebhookListenUrl,
    string? GatewayCertificatePath = null,
    string? WebhookPublicUrl = null)
{
    public const string LegacyPacsVendor = "LegacyPacs";
    public const string CloudPacsVendor = "CloudPacs";
    public const string DefaultWebhookListenUrl = "http://localhost:5186";
}
