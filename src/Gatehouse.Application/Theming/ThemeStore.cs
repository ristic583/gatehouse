using System.Text.Json;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Domain.Tenancy;
using Gatehouse.Domain.Theming;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Gatehouse.Application.Theming;

/// <summary>
/// The theme lives as JSON in <c>tenant_settings</c> key <see cref="Key"/>; missing or invalid → <see cref="TenantTheme.Default"/>.
/// </summary>
public sealed partial class ThemeStore(ITenantDb db, ILogger<ThemeStore> logger)
{
    public const string Key = "theme";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<TenantTheme> GetAsync(CancellationToken ct)
    {
        var row = await db.TenantSettings.AsNoTracking().SingleOrDefaultAsync(s => s.Key == Key, ct);
        if (row is null)
        {
            return TenantTheme.Default;
        }

        try
        {
            var dto = JsonSerializer.Deserialize<ThemeDto>(row.Value, Json);
            var theme = dto is null ? null : FromDto(dto);
            if (theme is { IsSuccess: true })
            {
                return theme.Value;
            }
        }
        catch (JsonException)
        {
            // fall through to the default
        }

        LogInvalid();
        return TenantTheme.Default;
    }

    /// <summary>Stages the change; the caller saves.</summary>
    public async Task SetAsync(TenantTheme theme, DateTimeOffset now, CancellationToken ct)
    {
        var value = JsonSerializer.Serialize(ToDto(theme), Json);
        var row = await db.TenantSettings.SingleOrDefaultAsync(s => s.Key == Key, ct);
        if (row is null)
        {
            db.TenantSettings.Add(new TenantSetting(Key, value, now));
        }
        else
        {
            row.Update(value, now);
        }
    }

    public static ThemeDto ToDto(TenantTheme t) =>
        new(t.Primary, t.PrimaryForeground, t.Accent, t.AccentForeground, t.Background, t.Foreground, t.RadiusPx, t.Terms);

    public static Domain.Common.Result<TenantTheme> FromDto(ThemeDto d) =>
        TenantTheme.Create(d.Primary, d.PrimaryForeground, d.Accent, d.AccentForeground, d.Background, d.Foreground, d.RadiusPx, d.Terms);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Stored tenant theme is invalid; using the default")]
    private partial void LogInvalid();
}
