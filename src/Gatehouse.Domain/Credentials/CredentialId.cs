namespace Gatehouse.Domain.Credentials;

public readonly record struct CredentialId(Guid Value)
{
    public static CredentialId New() => new(Guid.CreateVersion7());
}
