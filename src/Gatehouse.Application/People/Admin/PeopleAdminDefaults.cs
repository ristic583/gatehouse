using Gatehouse.Application.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.People.Admin;

public static class PeopleAdminDefaults
{
    public const string DefaultTimeZone = "UTC";

    /// <summary>New people get the zone of the first site (or UTC before any site exists).</summary>
    public static async Task<string> TimeZoneAsync(ITenantDb db, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        return await db.Sites.AsNoTracking().OrderBy(s => s.Name).Select(s => s.TimeZoneId).FirstOrDefaultAsync(ct) ?? DefaultTimeZone;
    }
}
