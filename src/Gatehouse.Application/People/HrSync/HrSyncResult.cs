namespace Gatehouse.Application.People.HrSync;

public sealed record HrSyncResult(int Created, int Updated, int Unchanged, int Skipped);
