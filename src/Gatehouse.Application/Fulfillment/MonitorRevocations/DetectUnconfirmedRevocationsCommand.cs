namespace Gatehouse.Application.Fulfillment.MonitorRevocations;

public sealed record DetectUnconfirmedRevocationsCommand(DateTimeOffset Now);
