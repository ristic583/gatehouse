using Gatehouse.Domain.People;

namespace Gatehouse.UnitTests.People;

public sealed class LifecycleSettingsTests
{
    [Fact]
    public void Max_days_per_type_follow_settings()
    {
        var settings = LifecycleSettings.Defaults.With(LifecycleSettings.MaxVisitorDaysKey, 3);

        Assert.Equal(365, settings.MaxDays(PersonType.External));
        Assert.Equal(365, settings.MaxDays(PersonType.Guest));
        Assert.Equal(3, settings.MaxDays(PersonType.Visitor));
        Assert.Null(settings.MaxDays(PersonType.Employee));
        Assert.Equal(1825, settings.RenewalDays(PersonType.Employee));
        Assert.Equal(30, settings.RenewalDays(PersonType.Visitor));
        Assert.Equal(6, LifecycleSettings.Keys.Count);
        Assert.All(LifecycleSettings.Keys, k => Assert.True(settings.Get(k) > 0));
    }
}
