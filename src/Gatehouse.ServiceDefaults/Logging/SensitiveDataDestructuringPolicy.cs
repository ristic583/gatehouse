using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Gatehouse.Domain.Common;
using Serilog.Core;
using Serilog.Events;

namespace Gatehouse.ServiceDefaults.Logging;

/// <summary>
/// Destructures objects that carry secrets: properties marked <see cref="SensitiveAttribute"/> and
/// well-known secret property names are rendered as <c>"***"</c>.
/// </summary>
public sealed class SensitiveDataDestructuringPolicy : IDestructuringPolicy
{
    public const string Mask = "***";

    private static readonly HashSet<string> SecretNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Pin",
        "Password",
        "Token",
        "AccessToken",
        "RefreshToken",
        "IdToken",
        "ClientSecret",
        "ConnectionString",
        "EncryptedConnectionString",
    };

    private static readonly ConcurrentDictionary<Type, PropertyPlan[]?> Plans = new();

    public bool TryDestructure(
        object value,
        ILogEventPropertyValueFactory propertyValueFactory,
        [NotNullWhen(true)] out LogEventPropertyValue? result)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(propertyValueFactory);

        var plan = Plans.GetOrAdd(value.GetType(), BuildPlan);
        if (plan is null)
        {
            result = null;
            return false;
        }

        var properties = new List<LogEventProperty>(plan.Length);
        foreach (var property in plan)
        {
            LogEventPropertyValue propertyValue;
            if (property.IsSensitive)
            {
                propertyValue = new ScalarValue(Mask);
            }
            else
            {
                object? raw;
                try
                {
                    raw = property.Info.GetValue(value);
                }
                catch (TargetInvocationException ex)
                {
                    raw = "The property accessor threw an exception: " + ex.InnerException?.GetType().Name;
                }

                propertyValue = propertyValueFactory.CreatePropertyValue(raw, destructureObjects: true);
            }

            properties.Add(new LogEventProperty(property.Info.Name, propertyValue));
        }

        result = new StructureValue(properties, value.GetType().Name);
        return true;
    }

    private static PropertyPlan[]? BuildPlan(Type type)
    {
        if (type.IsPrimitive || type.IsEnum || type == typeof(string))
        {
            return null;
        }

        var properties = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .Select(p => new PropertyPlan(p, IsSensitive(p)))
            .ToArray();

        return properties.Any(p => p.IsSensitive) ? properties : null;
    }

    private static bool IsSensitive(PropertyInfo property) =>
        property.IsDefined(typeof(SensitiveAttribute), inherit: true) || SecretNames.Contains(property.Name);

    private sealed record PropertyPlan(PropertyInfo Info, bool IsSensitive);
}
