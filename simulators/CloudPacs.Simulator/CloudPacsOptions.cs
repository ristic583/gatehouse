namespace CloudPacs.Simulator;

/// <summary>Simulator settings (section <c>CloudPacs</c>).</summary>
public sealed class CloudPacsOptions
{
    public const string DefaultApiKey = "dev-cloudpacs-key";

    public string ApiKey { get; set; } = DefaultApiKey;

    /// <summary>New items appear in list endpoints only after this delay (eventual consistency).</summary>
    public int ReadDelayMs { get; set; }

    public double ErrorRate { get; set; }

    public double RateLimitRate { get; set; }
}
