namespace Gatehouse.Domain.Access;

/// <summary>
/// The validity window a PACS should hold for one person and one access level. A PACS keeps one assignment per person and level
/// (LegacyPACS upserts it), so every approved window of that person and level shares it. Rule: take the window that ends first and
/// merge every window that overlaps or touches it, transitively. The current access is never shortened by a later (renewal) window;
/// a later window separated by a gap is written when the earlier one ends (its revoke re-grants the remainder).
/// </summary>
public readonly record struct AccessWindow(DateTimeOffset Start, DateTimeOffset End)
{
    /// <summary>The window to write for the given live windows, or <c>null</c> when there is none.</summary>
    public static AccessWindow? Current(IEnumerable<AccessWindow> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        var list = windows.Where(w => w.End > w.Start).ToList();
        if (list.Count == 0)
        {
            return null;
        }

        var anchor = list.OrderBy(w => w.End).ThenBy(w => w.Start).First();
        var start = anchor.Start;
        var end = anchor.End;
        bool changed;
        do
        {
            changed = false;
            foreach (var w in list)
            {
                if (w.Start <= end && w.End >= start && (w.Start < start || w.End > end))
                {
                    start = w.Start < start ? w.Start : start;
                    end = w.End > end ? w.End : end;
                    changed = true;
                }
            }
        }
        while (changed);

        return new AccessWindow(start, end);
    }
}
