namespace Gatehouse.UnitTests.Architecture;

/// <summary>Allow-lists only shrink: an entry that no longer violates its rule must be removed.</summary>
public sealed class AllowListTests
{
    private static readonly Dictionary<string, Func<IReadOnlyCollection<string>>> Violations = new()
    {
        [AllowList.HandlersClasses] = NamingRuleTests.HandlersClasses,
        [AllowList.SingleUseServices] = NamingRuleTests.SingleUseServices,
        [AllowList.ApiDataAccess] = LayerDependencyTests.ApiDataAccess,
        [AllowList.OversizeFiles] = SourceSizeTests.OversizeFiles,
        [AllowList.LongLines] = SourceSizeTests.FilesWithLongLines,
        [AllowList.CommandsWithoutValidators] = NamingRuleTests.CommandsWithoutValidators,
    };

    [Fact]
    public void Allow_lists_have_no_stale_entries()
    {
        Assert.Equal(AllowList.All.Order(), Violations.Keys.Order());

        var problems = new List<string>();
        foreach (var name in AllowList.All)
        {
            var entries = AllowList.Read(name);
            if (!entries.SequenceEqual(entries.Order(StringComparer.Ordinal)))
                problems.Add($"{name}.txt is not sorted");
            problems.AddRange(entries.GroupBy(e => e).Where(g => g.Count() > 1).Select(g => $"{name}.txt duplicate: {g.Key}"));

            var current = Violations[name]().ToHashSet(StringComparer.Ordinal);
            problems.AddRange(entries.Where(e => !current.Contains(e)).Select(e => $"{name}.txt stale: {e}"));
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }
}
