using Microsoft.AspNetCore.Authorization;

namespace Gatehouse.Application.Authorization.Requirements;

/// <summary>Resource = <see cref="Resources.PacsInstanceResource"/>: tenant admin, or security staff tenant-wide or with one of the
/// instance's sites in scope (authorization.md §10).</summary>
public sealed class ViewReconciliationRequirement : IAuthorizationRequirement;
