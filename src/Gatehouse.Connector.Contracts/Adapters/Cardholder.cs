using Gatehouse.Connector.Contracts.Events;

namespace Gatehouse.Connector.Contracts.Adapters;

public sealed record Cardholder(string PersonRef, string GivenName, string FamilyName, string? CardNumber, string? PhotoUrl);
