using Gatehouse.Application.Credentials.Dtos;

namespace Gatehouse.Application.People.Dtos;

/// <summary><c>CanIssueMagicLink</c> = can manage, <c>Type == Guest</c> and <c>Active</c>.</summary>
public sealed record PersonDetailDto(
    PersonSummaryDto Summary,
    IReadOnlyList<CardDto> Cards,
    bool CanManage,
    bool CanActivateCards,
    bool CanIssueMagicLink,
    IReadOnlyList<PersonEntitlementDto> Entitlements,
    bool CanIssueCards = false,
    bool CanDeactivateCards = false,
    bool CanTerminate = false);
