using Microsoft.AspNetCore.Authorization;

namespace Gatehouse.Application.Authorization.Requirements;

/// <summary>The caller belongs to a resolved, active tenant.</summary>
public sealed class TenantMemberRequirement : IAuthorizationRequirement;
