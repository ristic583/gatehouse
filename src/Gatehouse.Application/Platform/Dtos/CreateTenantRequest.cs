namespace Gatehouse.Application.Platform.Dtos;

public sealed record CreateTenantRequest(
    string TenantId,
    string DisplayName,
    string EmailDomain,
    string AdminEmail,
    string AdminGivenName,
    string AdminFamilyName,
    string TimeZoneId);
