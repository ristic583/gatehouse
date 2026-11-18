using FluentValidation;
using Gatehouse.Application.Common.Validation;

namespace Gatehouse.Application.Fulfillment.PacsAdmin.SetAccessLevelMappings;

/// <summary>Request shape of <c>PUT …/mappings</c>; whether the areas lie on the instance's sites needs the database (handler).</summary>
public sealed class SetAccessLevelMappingsValidator : AbstractValidator<List<AccessLevelMappingRequest>>
{
    public SetAccessLevelMappingsValidator()
    {
        RuleFor(r => r).Must(r => r.Select(m => m.AreaId).Distinct().Count() == r.Count)
            .WithProblem("mapping.invalid", "Each area may be mapped once.");
        RuleForEach(r => r).Must(m => !string.IsNullOrWhiteSpace(m.VendorAccessLevelRef))
            .WithProblem("mapping.invalid", "Vendor access level reference and display name are required.");
    }
}
