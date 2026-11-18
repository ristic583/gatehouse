using Gatehouse.Domain.Audit;

namespace Gatehouse.Application.Audit;

public interface IAuditLog
{
    public void Record(AuditActor actor, string action, string subjectType, string subjectId, object? payload = null);
}
