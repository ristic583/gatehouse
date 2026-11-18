using Gatehouse.Domain.Common;
using Gatehouse.Domain.Sites;

namespace Gatehouse.Domain.Tasks.Events;

public sealed record StaffTaskCreated(
    StaffTaskId TaskId,
    StaffTaskKind Kind,
    SiteId? SiteId,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
