using Microsoft.AspNetCore.Authorization;

namespace Gatehouse.Application.Authorization.Requirements;

/// <summary>Resource = <see cref="Resources.PacsInstanceResource"/>: security staff tenant-wide or with every site of the instance in
/// scope; tenant admin never (authorization.md §10).</summary>
public sealed class RunReconciliationRequirement : IAuthorizationRequirement;
