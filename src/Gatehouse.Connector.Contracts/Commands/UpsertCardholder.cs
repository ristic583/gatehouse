namespace Gatehouse.Connector.Contracts.Commands;

public sealed record UpsertCardholder(string PersonRef, string GivenName, string FamilyName, string? CardNumber, string? PhotoUrl);
