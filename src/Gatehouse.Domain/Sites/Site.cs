using System.Text.RegularExpressions;
using Gatehouse.Domain.Common;

namespace Gatehouse.Domain.Sites;

public sealed partial class Site : AggregateRoot<SiteId>
{
    private Site(SiteId id, string name, string code, string timeZoneId)
        : base(id)
    {
        Name = name;
        Code = code;
        TimeZoneId = timeZoneId;
    }

    public string Name { get; private set; }

    public string Code { get; private set; }

    public string TimeZoneId { get; private set; }

    public static Result<Site> Create(string name, string code, string timeZoneId)
    {
        var valid = Validate(name, code, timeZoneId);
        return valid.IsFailure ? valid.Error : new Site(SiteId.New(), name, code, timeZoneId);
    }

    public Result Update(string name, string code, string timeZoneId)
    {
        var valid = Validate(name, code, timeZoneId);
        if (valid.IsFailure)
        {
            return valid;
        }

        Name = name;
        Code = code;
        TimeZoneId = timeZoneId;
        return Result.Success();
    }

    private static Result Validate(string name, string code, string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 100)
        {
            return Error.Validation("site.invalid_name", "Site name must be 1-100 characters.");
        }

        if (code is null || !CodePattern().IsMatch(code))
        {
            return Error.Validation("site.invalid_code", "Site code must match ^[A-Z0-9-]{2,20}$.");
        }

        if (string.IsNullOrWhiteSpace(timeZoneId) || !TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out _))
        {
            return Error.Validation("site.invalid_time_zone", "Time zone must be a valid IANA id.");
        }

        return Result.Success();
    }

    [GeneratedRegex("^[A-Z0-9-]{2,20}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodePattern();
}
