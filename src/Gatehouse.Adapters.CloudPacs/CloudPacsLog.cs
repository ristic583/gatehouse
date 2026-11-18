using Gatehouse.Connector.Contracts.Adapters;
using Microsoft.Extensions.Logging;

namespace Gatehouse.Adapters.CloudPacs;

internal static partial class CloudPacsLog
{
    [LoggerMessage(Level = LogLevel.Information, Message = "CloudPACS {Operation} for card {Card} finished with {Status}")]
    public static partial void Write(ILogger logger, string operation, string card, AdapterStatus status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "CloudPACS {Operation} failed: {Code}")]
    public static partial void Failed(ILogger logger, string operation, string code);

    [LoggerMessage(Level = LogLevel.Information, Message = "CloudPACS webhook listener bound to {Address}")]
    public static partial void ListenerStarted(ILogger logger, string address);

    [LoggerMessage(Level = LogLevel.Information, Message = "CloudPACS webhook registered as {WebhookId} for {Url}")]
    public static partial void WebhookRegistered(ILogger logger, string webhookId, string url);

    [LoggerMessage(Level = LogLevel.Warning, Message = "CloudPACS webhook registration failed ({Code}); retrying in {Delay}")]
    public static partial void WebhookRegistrationFailed(ILogger logger, string code, TimeSpan delay);

    [LoggerMessage(Level = LogLevel.Warning, Message = "CloudPACS webhook rejected: invalid signature")]
    public static partial void BadSignature(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "CloudPACS webhook rejected: unreadable payload")]
    public static partial void BadPayload(ILogger logger);

    [LoggerMessage(Level = LogLevel.Debug, Message = "CloudPACS event type {Type} is not mapped; skipped")]
    public static partial void UnknownEventType(ILogger logger, string type);
}
