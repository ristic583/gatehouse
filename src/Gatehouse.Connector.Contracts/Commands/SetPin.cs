namespace Gatehouse.Connector.Contracts.Commands;

public sealed record SetPin(string PersonRef, string CardNumber, string EncryptedPin);
