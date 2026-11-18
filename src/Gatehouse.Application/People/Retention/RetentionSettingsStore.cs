using System.Globalization;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.People.Retention;

/// <summary>Reads and stages <c>retention.*</c> settings; missing or out-of-range values use <see
/// cref="RetentionSettings.Defaults"/>.</summary>
public sealed class RetentionSettingsStore(ITenantDb db)
{
    public async Task<RetentionSettings> GetAsync(CancellationToken ct)
    {
        var keys = RetentionSettings.Ranges.Keys.ToList();
        var rows = await db.TenantSettings.AsNoTracking().Where(s => keys.Contains(s.Key)).ToListAsync(ct);
        var settings = RetentionSettings.Defaults;
        foreach (var row in rows)
        {
            var (min, max) = RetentionSettings.Ranges[row.Key];
            if (int.TryParse(row.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days) && days >= min && days <= max)
            {
                settings = settings.With(row.Key, days);
            }
        }

        return settings;
    }

    /// <summary>Stages the change; the caller saves.</summary>
    public async Task<Result> SetAsync(string? key, int days, DateTimeOffset now, CancellationToken ct)
    {
        if (key is null || !RetentionSettings.Ranges.TryGetValue(key, out var range) || days < range.Min || days > range.Max)
        {
            return Error.Validation("retention.invalid_setting", "Unknown retention setting or a value outside its range.");
        }

        var value = days.ToString(CultureInfo.InvariantCulture);
        var row = await db.TenantSettings.SingleOrDefaultAsync(s => s.Key == key, ct);
        if (row is null)
        {
            db.TenantSettings.Add(new TenantSetting(key, value, now));
        }
        else
        {
            row.Update(value, now);
        }

        return Result.Success();
    }
}
