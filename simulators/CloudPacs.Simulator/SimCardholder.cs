namespace CloudPacs.Simulator;

public sealed record SimCardholder(Guid Id, string ExternalRef, string GivenName, string FamilyName, string? PhotoUrl, DateTimeOffset CreatedAt);
