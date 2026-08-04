using MotoBudsManager.Core;

namespace MotoBudsManager.Tests;

public sealed class LowBatteryAlertTrackerTests
{
    [Fact]
    public void Evaluate_BelowTwentyAndNotCharging_ReturnsNewComponents()
    {
        var tracker = new LowBatteryAlertTracker();
        var state = new EarbudBatteryState(
            new(19, false),
            new(5, true),
            new(12, false));

        var alert = tracker.Evaluate("device", state);

        Assert.NotNull(alert);
        Assert.Equal(BatteryComponentKind.Left | BatteryComponentKind.Case, alert.Components);
        Assert.Equal(12, alert.LowestPercent);
    }

    [Fact]
    public void Evaluate_AtTwentyOrCharging_DoesNotAlert()
    {
        var tracker = new LowBatteryAlertTracker();
        var state = new EarbudBatteryState(
            new(20, false),
            new(1, true),
            new(null, false));

        Assert.Null(tracker.Evaluate("device", state));
    }

    [Fact]
    public void Evaluate_DeduplicatesUntilBatteryRecovers()
    {
        var tracker = new LowBatteryAlertTracker();
        var low = new EarbudBatteryState(new(19, false), new(50, false), new(50, false));
        var recovered = new EarbudBatteryState(new(25, false), new(50, false), new(50, false));

        Assert.NotNull(tracker.Evaluate("device", low));
        Assert.Null(tracker.Evaluate("device", low));
        Assert.Null(tracker.Evaluate("device", recovered));
        Assert.NotNull(tracker.Evaluate("device", low));
    }
}
