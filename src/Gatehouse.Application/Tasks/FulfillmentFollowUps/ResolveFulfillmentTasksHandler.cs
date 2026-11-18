using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;

namespace Gatehouse.Application.Tasks.FulfillmentFollowUps;

/// <summary>Consumer of <c>FulfillmentConfirmed</c> (ADR-008): auto-resolves open tasks of the fulfillment; the consumer saves.</summary>
public sealed class ResolveFulfillmentTasksHandler(StaffTaskService tasks)
{
    public Task HandleAsync(ResolveFulfillmentTasksCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var parsed = Guid.TryParse(command.ConfirmedById, out var id);
        PersonId? by = command.ConfirmedByKind == ConfirmerKind.Staff && parsed ? new PersonId(id) : null;
        var actor = by is { } person ? AuditActor.ForPerson(person)
            : command.ConfirmedByKind == ConfirmerKind.Agent && parsed ? AuditActor.ForAgent(new ConnectorId(id))
            : AuditActor.ForSystem("fulfillment-consumer");
        return tasks.ResolveForSubjectAsync("Fulfillment", command.FulfillmentId.Value, by, actor, command.Now, ct);
    }
}
