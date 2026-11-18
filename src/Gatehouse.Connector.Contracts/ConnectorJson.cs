using System.Text.Json;
using System.Text.Json.Serialization;
using Gatehouse.Connector.Contracts.Protocol;

namespace Gatehouse.Connector.Contracts;

public static class ConnectorJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        o.Converters.Add(new JsonStringEnumConverter());
        return o;
    }

    public static T DeserializePayload<T>(CommandEnvelope envelope) =>
        envelope.Payload.Deserialize<T>(Options) ?? throw new JsonException("Command payload is null.");
}
