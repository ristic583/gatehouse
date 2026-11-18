namespace Gatehouse.Agent.Execution;

/// <summary>The gateway will never accept this export (unknown or closed); the command is acked as failed, not retryable.</summary>
public sealed class ExportRejectedException(string code) : Exception($"The gateway rejected the export ({code})")
{
    public string Code { get; } = code;
}
