using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gatehouse.IntegrationTests.Infrastructure;

/// <summary>Matches the API's HTTP JSON (web defaults + enums as strings).</summary>
public static class ApiJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
}
