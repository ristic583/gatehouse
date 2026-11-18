namespace CloudPacs.Simulator;

public sealed record SimCard(string CardNumber, string ExternalRef, string State, DateTimeOffset CreatedAt);
