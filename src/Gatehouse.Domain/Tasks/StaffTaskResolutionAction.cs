namespace Gatehouse.Domain.Tasks;

public enum StaffTaskResolutionAction
{
    AutoResolved,
    Retried,
    ConfirmedManually,
    VerifiedInPacs,
    Dismissed,
    Remediated,
    RemovedFromPacs,
}
