using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tenancy;

namespace Gatehouse.Application.Tenancy;

public sealed record TenantSummary(TenantId Id, string DisplayName, TenantStatus Status, IdpKind IdpKind);
