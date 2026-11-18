using Gatehouse.Connector.Contracts.Events;

namespace Gatehouse.Connector.Contracts.Adapters;

public sealed class SensitivePin(string value)
{
    public string Reveal() => value;

    public override string ToString() => "****";
}
