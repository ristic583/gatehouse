using Gatehouse.Domain.Tasks;

namespace Gatehouse.Application.Reconciliation.Remediation;

/// <summary>Task kinds remediated by <c>ResolveStaffTaskHandler</c> and the audit subject of remediation entries.</summary>
public static class ReconciliationRemediation
{
    public const string AuditSubject = "ReconciliationFinding";

    public static bool Handles(StaffTaskKind kind) => kind is StaffTaskKind.ReconciliationFinding or StaffTaskKind.DriftAlert;
}
