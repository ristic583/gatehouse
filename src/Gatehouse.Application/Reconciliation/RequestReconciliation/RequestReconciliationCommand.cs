using Gatehouse.Domain.Fulfillment;

namespace Gatehouse.Application.Reconciliation.RequestReconciliation;

public sealed record RequestReconciliationCommand(PacsInstanceId PacsInstanceId);
