using Gatehouse.Application.Audit;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Identity;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Theming;

namespace Gatehouse.Application.Theming.SaveTheme;

/// <summary>
/// <c>PUT /api/v1/admin/theme</c>: validate → WCAG AA check → save → audit <c>theme.changed</c>. Returns
/// <see cref="SaveThemeResult"/> because contrast failures carry per-pair details next to the error.
/// </summary>
public sealed class SaveThemeHandler(
    ITenantDb db,
    ThemeStore store,
    ICurrentUser currentUser,
    ITenantContext tenant,
    IAuditLog audit,
    TimeProvider time)
{
    public async Task<SaveThemeResult> HandleAsync(SaveThemeCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Theme);
        var theme = ThemeStore.FromDto(command.Theme);
        if (theme.IsFailure)
        {
            return new SaveThemeResult(null, theme.Error, []);
        }

        var failing = ContrastChecker.Check(theme.Value);
        if (failing.Count > 0)
        {
            return new SaveThemeResult(null, null, failing);
        }

        var before = await store.GetAsync(ct);
        var me = await currentUser.RequirePersonIdAsync(ct);
        await store.SetAsync(theme.Value, time.GetUtcNow(), ct);
        var payload = new { changed = Changed(before, theme.Value) };
        audit.Record(AuditActor.ForPerson(me), "theme.changed", "Tenant", tenant.TenantId.Value, payload);
        await db.SaveChangesAsync(ct);
        return new SaveThemeResult(ThemeStore.ToDto(theme.Value), null, []);
    }

    private static List<string> Changed(TenantTheme a, TenantTheme b)
    {
        var changed = new List<string>();
        void Check(string name, object x, object y)
        {
            if (!Equals(x, y))
            {
                changed.Add(name);
            }
        }

        Check("primary", a.Primary, b.Primary);
        Check("primaryForeground", a.PrimaryForeground, b.PrimaryForeground);
        Check("accent", a.Accent, b.Accent);
        Check("accentForeground", a.AccentForeground, b.AccentForeground);
        Check("background", a.Background, b.Background);
        Check("foreground", a.Foreground, b.Foreground);
        Check("radiusPx", a.RadiusPx, b.RadiusPx);
        var termsBefore = a.Terms.OrderBy(t => t.Key, StringComparer.Ordinal);
        if (!termsBefore.SequenceEqual(b.Terms.OrderBy(t => t.Key, StringComparer.Ordinal)))
        {
            changed.Add("terms");
        }

        return changed;
    }
}
