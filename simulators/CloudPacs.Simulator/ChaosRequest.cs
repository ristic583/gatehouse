namespace CloudPacs.Simulator;

public sealed record ChaosRequest(int? FailNext, int? RateLimitNext, double? ErrorRate, double? RateLimitRate);
