namespace Gatehouse.Application.People.Lifecycle;

public sealed record LifecycleRunResult(int Terminated, int Expired, int RenewalsRequested);
