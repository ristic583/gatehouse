using Gatehouse.Application.Connectors.Messages;
using Gatehouse.Domain.Common;

namespace Gatehouse.Infrastructure.Messaging;

/// <summary>Maps <c>outbox_messages.type</c> (CLR type name) to the CLR type.</summary>
public sealed class OutboxMessageTypeRegistry
{
    private readonly Dictionary<string, Type> _types = new(StringComparer.Ordinal);

    public OutboxMessageTypeRegistry()
    {
        var candidates = typeof(IDomainEvent).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(IDomainEvent).IsAssignableFrom(t))
            .Concat(typeof(ConnectorCommandAcked).Assembly.GetTypes()
                .Where(t => t is { IsAbstract: false, IsInterface: false } && t.Namespace == typeof(ConnectorCommandAcked).Namespace
                && !t.Name.StartsWith('<')));
        foreach (var type in candidates)
        {
            if (!_types.TryAdd(type.Name, type))
            {
                throw new InvalidOperationException($"Duplicate outbox message type name '{type.Name}'.");
            }
        }
    }

    public Type Resolve(string name) =>
        _types.TryGetValue(name, out var type) ? type : throw new InvalidOperationException($"Unknown outbox message type '{name}'.");
}
