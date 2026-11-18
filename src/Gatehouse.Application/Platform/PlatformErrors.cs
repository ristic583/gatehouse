using Gatehouse.Domain.Common;
using Gatehouse.Domain.Platform;

namespace Gatehouse.Application.Platform;

public static class PlatformErrors
{
    public static Error TenantNotFound { get; } = Error.NotFound("tenant.not_found", "The tenant does not exist.");

    /// <summary>Route ids that are not valid slugs cannot exist in the catalog.</summary>
    public static Result<TenantId> ParseTenantId(string value) =>
        TenantId.Create(value) is { IsSuccess: true } id ? id.Value : TenantNotFound;

    public static PlatformActor AsActor(this IPlatformOperator platformOperator)
    {
        ArgumentNullException.ThrowIfNull(platformOperator);
        return new PlatformActor(PlatformActorKind.Operator, platformOperator.Id.Value.ToString());
    }
}
