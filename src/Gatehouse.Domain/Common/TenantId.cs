using System.Text.RegularExpressions;

namespace Gatehouse.Domain.Common;

public readonly partial record struct TenantId(string Value)
{
    public string DatabaseName => "gh_tenant_" + Value.Replace('-', '_');

    public static Result<TenantId> Create(string value)
    {
        if (value is null || !SlugPattern().IsMatch(value))
        {
            return Error.Validation(
                "tenant.invalid_id",
                "Tenant id must be 3-32 characters: a lowercase letter followed by lowercase letters, digits or dashes.");
        }

        return new TenantId(value);
    }

    public override string ToString() => Value;

    [GeneratedRegex("^[a-z][a-z0-9-]{2,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();
}
