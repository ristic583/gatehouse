namespace Gatehouse.Application.People.Admin;

/// <summary>
/// Thrown by the tenant unit of work when a save would remove the last <c>TenantAdmin</c> assignment; the check runs under the tenant's
/// audit lock so concurrent removals are serialized (GH-AUTH-05). Handlers map it to <c>role.last_tenant_admin</c>.
/// </summary>
public sealed class LastTenantAdminException : InvalidOperationException
{
    public LastTenantAdminException()
        : base("The last tenant admin cannot be removed.")
    {
    }

    public LastTenantAdminException(string message)
        : base(message)
    {
    }

    public LastTenantAdminException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
