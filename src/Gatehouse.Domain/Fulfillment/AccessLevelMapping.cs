using Gatehouse.Domain.Common;
using Gatehouse.Domain.Sites;

namespace Gatehouse.Domain.Fulfillment;

public sealed class AccessLevelMapping : Entity<AccessLevelMappingId>
{
    private AccessLevelMapping(
        AccessLevelMappingId id, AreaId areaId, PacsInstanceId pacsInstanceId, string vendorAccessLevelRef, string displayName)
        : base(id)
    {
        AreaId = areaId;
        PacsInstanceId = pacsInstanceId;
        VendorAccessLevelRef = vendorAccessLevelRef;
        DisplayName = displayName;
    }

    public AreaId AreaId { get; private set; }

    public PacsInstanceId PacsInstanceId { get; private set; }

    public string VendorAccessLevelRef { get; private set; }

    public string DisplayName { get; private set; }

    public Result Update(string vendorAccessLevelRef, string displayName)
    {
        if (string.IsNullOrWhiteSpace(vendorAccessLevelRef) || string.IsNullOrWhiteSpace(displayName))
        {
            return Error.Validation("mapping.invalid", "Vendor access level reference and display name are required.");
        }

        VendorAccessLevelRef = vendorAccessLevelRef;
        DisplayName = displayName;
        return Result.Success();
    }

    public static Result<AccessLevelMapping> Create(
        AreaId areaId, PacsInstanceId pacsInstanceId, string vendorAccessLevelRef, string displayName)
    {
        if (string.IsNullOrWhiteSpace(vendorAccessLevelRef) || string.IsNullOrWhiteSpace(displayName))
        {
            return Error.Validation("mapping.invalid", "Vendor access level reference and display name are required.");
        }

        return new AccessLevelMapping(AccessLevelMappingId.New(), areaId, pacsInstanceId, vendorAccessLevelRef, displayName);
    }
}
