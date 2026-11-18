namespace Gatehouse.UnitTests.Architecture;

/// <summary>
/// conventions.md §7: C# files over 400 lines must be split; lines stay within 140 characters. conventions.md §3: every
/// <c>#pragma warning disable</c> carries a one-line reason.
/// </summary>
public sealed class SourceSizeTests
{
    private const int MaxFileLines = 400;
    private const int MaxLineLength = 140;

    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.Ordinal) { "Migrations", "obj", "bin", "node_modules" };

    private static readonly Lazy<IReadOnlyList<(string Path, string[] Lines)>> Sources = new(() => LoadSources("src"));

    private static readonly Lazy<IReadOnlyList<(string Path, string[] Lines)>> AllSources =
        new(() => [.. LoadSources("src"), .. LoadSources("simulators"), .. LoadSources("tests")]);

    [Fact]
    public void Csharp_files_stay_within_400_lines() =>
        AllowList.AssertCovers(AllowList.OversizeFiles, OversizeFiles());

    [Fact]
    public void Csharp_lines_stay_within_140_characters() =>
        AllowList.AssertCovers(AllowList.LongLines, FilesWithLongLines());

    [Fact]
    public void Suppressions_carry_a_reason()
    {
        var unjustified = AllSources.Value
            .SelectMany(s => UnjustifiedSuppressions(s.Lines).Select(line => $"{s.Path}:{line}"))
            .ToList();
        Assert.True(
            unjustified.Count == 0,
            $"#pragma warning disable without a '// reason' on the same or previous line:{Environment.NewLine}"
            + string.Join(Environment.NewLine, unjustified));
    }

    [Theory]
    [InlineData(new[] { "#pragma warning disable CA1031", "catch (Exception ex)" }, 1)]
    [InlineData(new[] { "var x = 1;", "#pragma warning disable CA1031" }, 1)]
    [InlineData(new[] { "#pragma warning disable CA1031 // host boundary: logged and retried" }, 0)]
    [InlineData(new[] { "// host boundary: logged and retried", "    #pragma warning disable CA1031" }, 0)]
    [InlineData(new[] { "#pragma warning restore CA1031" }, 0)]
    public void Suppression_rule_flags_a_missing_reason(string[] lines, int expected) =>
        Assert.Equal(expected, UnjustifiedSuppressions(lines).Count);

    /// <summary>1-based line numbers of <c>#pragma warning disable</c> lines with no <c>//</c> comment on the line or above it.</summary>
    internal static IReadOnlyList<int> UnjustifiedSuppressions(string[] lines) =>
        Enumerable.Range(0, lines.Length)
            .Where(i => lines[i].TrimStart().StartsWith("#pragma warning disable", StringComparison.Ordinal)
                && !lines[i].Contains("//", StringComparison.Ordinal)
                && (i == 0 || !lines[i - 1].TrimStart().StartsWith("//", StringComparison.Ordinal)))
            .Select(i => i + 1)
            .ToList();

    internal static IReadOnlyCollection<string> OversizeFiles() =>
        Sources.Value.Where(s => s.Lines.Length > MaxFileLines).Select(s => s.Path).ToList();

    internal static IReadOnlyCollection<string> FilesWithLongLines() =>
        Sources.Value.Where(s => s.Lines.Any(l => l.Length > MaxLineLength)).Select(s => s.Path).ToList();

    private static List<(string Path, string[] Lines)> LoadSources(string directory)
    {
        var root = AllowList.RepoRoot;
        var sources = CsharpFiles(Path.Combine(root, directory))
            .Select(f => (Path.GetRelativePath(root, f).Replace('\\', '/'), File.ReadAllLines(f)))
            .ToList();
        Assert.NotEmpty(sources);
        return sources;
    }

    private static IEnumerable<string> CsharpFiles(string directory) =>
        Directory.EnumerateFiles(directory, "*.cs")
            .Where(f => !f.EndsWith(".Designer.cs", StringComparison.Ordinal))
            .Concat(Directory.EnumerateDirectories(directory)
                .Where(d => !SkippedDirectories.Contains(Path.GetFileName(d)) && !Path.GetFileName(d).StartsWith('.'))
                .SelectMany(CsharpFiles));
}
