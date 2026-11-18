namespace Gatehouse.Application.Authorization;

public static class Policies
{
    public const string TenantMember = "TenantMember";
    public const string LinkedPerson = "LinkedPerson";
    public const string TenantAdmin = "TenantAdmin";
    public const string SecurityStaff = "SecurityStaff";
    public const string StaffConsole = "StaffConsole";
    public const string AuditReader = "AuditReader";
    public const string PlatformOperator = "PlatformOperator";
}
