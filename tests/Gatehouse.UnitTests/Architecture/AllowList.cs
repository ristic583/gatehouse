using System.Reflection;

namespace Gatehouse.UnitTests.Architecture;

/// <summary>
/// Known violations of conventions.md §1, §2, §3, §7, one file per rule in <c>Allowlists/</c>. Entries are sorted, one per
/// line; <c>#</c> starts a comment (the plan item that removes the entry). Lists only shrink: a stale entry fails.
/// </summary>
internal static class AllowList
{
    public const string HandlersClasses = "handlers-classes";
    public const string SingleUseServices = "single-use-services";
    public const string ApiDataAccess = "api-data-access";
    public const string OversizeFiles = "oversize-files";
    public const string LongLines = "long-lines";
    public const string CommandsWithoutValidators = "commands-without-validators";

    public static readonly string[] All = [HandlersClasses, SingleUseServices, ApiDataAccess, OversizeFiles, LongLines, CommandsWithoutValidators];

    public static IReadOnlyList<string> Read(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Architecture", "Allowlists", name + ".txt");
        return File.ReadAllLines(path)
            .Select(line => (line.IndexOf('#', StringComparison.Ordinal) is var hash and >= 0 ? line[..hash] : line).Trim())
            .Where(line => line.Length > 0)
            .ToList();
    }

    /// <summary>Asserts that every current violation is allow-listed (stale entries are checked by <see cref="AllowListTests"/>).</summary>
    public static void AssertCovers(string name, IEnumerable<string> violations)
    {
        var allowed = Read(name).ToHashSet(StringComparer.Ordinal);
        var unlisted = violations.Where(v => !allowed.Contains(v)).Order(StringComparer.Ordinal).ToList();
        Assert.True(unlisted.Count == 0, $"Not allowed ({name}.txt):{Environment.NewLine}{string.Join(Environment.NewLine, unlisted)}");
    }

    public static string RepoRoot { get; } = typeof(AllowList).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "RepoRoot").Value!;
}
