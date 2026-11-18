namespace Gatehouse.Adapters.CloudPacs;

/// <summary>CloudPACS connection settings. <see cref="WebhookListenUrl"/> may use port 0 (tests).</summary>
public sealed record CloudPacsAdapterOptions(
    Uri BaseUrl,
    string ApiKey,
    string WebhookListenUrl,
    string? WebhookPublicUrl = null,
    TimeSpan? RequestTimeout = null);
