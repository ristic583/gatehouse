using Gatehouse.Application.People.Dtos;
using Gatehouse.Domain.Reconciliation;

namespace Gatehouse.Application.Reconciliation.Dtos;

public sealed record ReconciliationFindingDto(
    Guid FindingId,
    string Kind,
    PersonRefDto? Person,
    string? PersonRef,
    Guid? AreaId,
    string? AreaName,
    string VendorAccessLevelRef,
    string? PacsRef,
    IReadOnlyList<string> Differences,
    FindingValues? Expected,
    FindingValues? Actual,
    Guid StaffTaskId,
    string TaskStatus,
    string? ResolutionAction);
