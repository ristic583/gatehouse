using Building.Simulator;

namespace Gatehouse.IntegrationTests.Scenarios.Infrastructure;

public static class ScenarioAsserts
{
    public static void Granted(ControllerDecision decision, string because)
    {
        ArgumentNullException.ThrowIfNull(decision);
        Assert.True(decision.Decision == "Granted", $"Expected the swipe to be granted ({because}), the controller said {decision.Decision}.");
    }

    public static void Denied(ControllerDecision decision, string because)
    {
        ArgumentNullException.ThrowIfNull(decision);
        Assert.True(decision.Decision == "Denied", $"Expected the swipe to be denied ({because}), the controller said {decision.Decision}.");
    }
}
