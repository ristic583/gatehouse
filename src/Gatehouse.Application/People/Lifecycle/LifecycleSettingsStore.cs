using System.Globalization;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Gatehouse.Application.People.Lifecycle;

/// <summary>Reads and writes the tenant's lifecycle settings (<c>tenant_settings</c> keys <c>lifecycle.*</c>); missing keys use spec
/// defaults.</summary>
public sealed partial class LifecycleSettingsStore(ITenantDb db, ILogger<LifecycleSettingsStore> logger)
{
    public const int MinDays = 1;
    public const int MaxDays = 3650;

    public async Task<LifecycleSettings> GetAsync(CancellationToken ct)
    {
        var keys = LifecycleSettings.Keys.ToList();
        var stored = await db.TenantSettings.AsNoTracking().Where(s => keys.Contains(s.Key)).ToListAsync(ct);
        var settings = LifecycleSettings.Defaults;
        foreach (var row in stored)
        {
            if (int.TryParse(row.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days)
                && days is >= MinDays and <= MaxDays)
            {
                settings = settings.With(row.Key, days);
            }
            else
            {
                LogInvalid(row.Key);
            }
        }

        return settings;
    }

    /// <summary>Stages the change; the caller saves.</summary>
    public async Task<Result> SetAsync(string key, int days, DateTimeOffset now, CancellationToken ct)
    {
        if (!LifecycleSettings.Keys.Contains(key) || days is < MinDays or > MaxDays)
        {
            return Error.Validation("lifecycle.invalid_setting", $"Unknown setting or a value outside {MinDays}-{MaxDays} days.");
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Lifecycle setting {Key} has an invalid value; using the default")]
    private partial void LogInvalid(string key);
}
