namespace Gatehouse.UnitTests;

public sealed class ScaffoldSmokeTests
{
    [Fact]
    public void Domain_assembly_is_loadable()
    {
        Assert.NotNull(typeof(Gatehouse.Domain.AssemblyMarker).Assembly);
    }
}
