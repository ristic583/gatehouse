using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gatehouse.IntegrationTests.Scenarios.Infrastructure;

internal static class ScenarioJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
}
