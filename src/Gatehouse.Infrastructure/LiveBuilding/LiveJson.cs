using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gatehouse.Infrastructure.LiveBuilding;

/// <summary>JSON used for live state in Redis (camelCase, enums as strings).</summary>
internal static class LiveJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
