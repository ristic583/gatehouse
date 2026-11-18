using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.People;

namespace Gatehouse.Domain.Audit;

public readonly record struct AuditActor(AuditActorKind Kind, string Id)
{
    public static AuditActor ForPerson(PersonId id) => new(AuditActorKind.Person, id.Value.ToString());

    public static AuditActor ForAgent(ConnectorId id) => new(AuditActorKind.Agent, id.Value.ToString());

    public static AuditActor ForSystem(string component) => new(AuditActorKind.System, component);
}
