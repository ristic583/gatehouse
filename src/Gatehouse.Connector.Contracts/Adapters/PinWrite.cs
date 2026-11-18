using Gatehouse.Connector.Contracts.Events;

namespace Gatehouse.Connector.Contracts.Adapters;

public sealed record PinWrite(string PersonRef, string CardNumber, SensitivePin Pin);
