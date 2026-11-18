namespace CloudPacs.Simulator;

public sealed record CardholderRequest(string GivenName, string FamilyName, string? CardNumber, string? PhotoUrl);
