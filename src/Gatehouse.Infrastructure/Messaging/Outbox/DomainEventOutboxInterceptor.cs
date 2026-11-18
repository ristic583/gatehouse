using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Gatehouse.Infrastructure.Messaging.Outbox;

/// <summary>Writes the domain events of tracked aggregates to <c>outbox_messages</c> in the same SaveChanges (ADR-005).</summary>
public sealed class DomainEventOutboxInterceptor : SaveChangesInterceptor
{
    private static readonly ConcurrentDictionary<Type, (PropertyInfo Events, MethodInfo Clear)?> Accessors = new();

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context is { } context)
        {
            WriteOutbox(context);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context is { } context)
        {
            WriteOutbox(context);
        }

        return base.SavingChanges(eventData, result);
    }

    private static void WriteOutbox(DbContext context)
    {
        var messages = new List<OutboxMessage>();
        foreach (var entry in context.ChangeTracker.Entries().ToList())
        {
            if (Accessor(entry.Entity.GetType()) is not { } accessor)
            {
                continue;
            }

            var events = (IReadOnlyList<IDomainEvent>)accessor.Events.GetValue(entry.Entity)!;
            foreach (var domainEvent in events)
            {
                messages.Add(new OutboxMessage
                {
                    Id = domainEvent.EventId,
                    OccurredAt = domainEvent.OccurredAt,
                    Type = domainEvent.GetType().Name,
                    Payload = JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), OutboxJson.Options),
                    Priority = CommandPriority.Normal,
                    NextAttemptAt = domainEvent.OccurredAt,
                    TraceParent = Gatehouse.Application.Common.Diagnostics.GatehouseTelemetry.CurrentTraceParent(),
                });
            }

            accessor.Clear.Invoke(entry.Entity, null);
        }

        if (messages.Count > 0)
        {
            context.Set<OutboxMessage>().AddRange(messages);
        }
    }

    private static (PropertyInfo Events, MethodInfo Clear)? Accessor(Type type) =>
        Accessors.GetOrAdd(type, static t =>
        {
            for (var current = t; current is not null; current = current.BaseType)
            {
                if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(AggregateRoot<>))
                {
                    return (current.GetProperty(nameof(AggregateRoot<int>.DomainEvents))!,
                        current.GetMethod(nameof(AggregateRoot<int>.ClearDomainEvents))!);
                }
            }

            return null;
        });
}
