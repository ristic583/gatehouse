using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gatehouse.Infrastructure.Messaging.Outbox;

public static class OutboxJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new StronglyTypedIdJsonConverterFactory());
        return options;
    }
}

/// <summary>Writes strongly typed ids (<c>record struct XId(Guid Value)</c>) as plain GUIDs.</summary>
public sealed class StronglyTypedIdJsonConverterFactory : JsonConverterFactory
{
    private static readonly ConcurrentDictionary<Type, bool> Cache = new();

    public override bool CanConvert(Type typeToConvert) =>
        Cache.GetOrAdd(typeToConvert, t => t.IsValueType
            && t.Name.EndsWith("Id", StringComparison.Ordinal)
            && t.GetProperty("Value")?.PropertyType == typeof(Guid)
            && t.GetConstructor([typeof(Guid)]) is not null);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
        (JsonConverter)Activator.CreateInstance(typeof(IdConverter<>).MakeGenericType(typeToConvert))!;

#pragma warning disable CA1812 // Created through reflection.
    private sealed class IdConverter<T> : JsonConverter<T>
#pragma warning restore CA1812
        where T : struct
    {
        private static readonly System.Reflection.PropertyInfo ValueProperty = typeof(T).GetProperty("Value")!;

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            (T)Activator.CreateInstance(typeof(T), reader.GetGuid())!;

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
            writer.WriteStringValue((Guid)ValueProperty.GetValue(value)!);
    }
}
