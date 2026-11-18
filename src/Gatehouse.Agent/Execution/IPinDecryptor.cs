using Gatehouse.Connector.Contracts.Adapters;

namespace Gatehouse.Agent.Execution;

public interface IPinDecryptor
{
    public SensitivePin Decrypt(string encryptedPin);
}
