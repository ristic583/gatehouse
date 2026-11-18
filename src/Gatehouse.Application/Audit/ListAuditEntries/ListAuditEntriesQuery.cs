namespace Gatehouse.Application.Audit.ListAuditEntries;

public sealed record ListAuditEntriesQuery(string? SubjectType, string? SubjectId, long? BeforeSequence, int Limit = 50);
