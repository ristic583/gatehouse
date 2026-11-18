namespace Gatehouse.Application.Tenancy;

public sealed class TenantNotResolvedException : Exception
{
    public TenantNotResolvedException()
        : base("No tenant is resolved for the current scope.")
    {
    }

    public TenantNotResolvedException(string message)
        : base(message)
    {
    }

    public TenantNotResolvedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
