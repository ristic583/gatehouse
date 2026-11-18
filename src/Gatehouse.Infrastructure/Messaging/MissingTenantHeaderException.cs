namespace Gatehouse.Infrastructure.Messaging;

public sealed class MissingTenantHeaderException : Exception
{
    public MissingTenantHeaderException()
        : base("Message has no gh-tenant-id header.")
    {
    }

    public MissingTenantHeaderException(string message)
        : base(message)
    {
    }

    public MissingTenantHeaderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
