using MotoBudsManager.Core;

namespace MotoBudsManager.Tests;

public sealed class LowBatteryAlertTrackerTests
{
    [Fact]
    public void Evaluate_EarbudAtFiveAndNotCharging_ShowsSystemNotification()
    {
        var tracker = new LowBatteryAlertTracker();
        var state = Battery(left: 5, right: 70, @case: 2);

        var evaluation = tracker.Evaluate("device", state);

        Assert.Equal(LowBatterySystemAction.Show, evaluation.SystemAction);
        Assert.Equal(5, evaluation.LowestPercent);
        Assert.False(evaluation.ShowInAppToast);
    }

    [Fact]
    public void Evaluate_CaseLow_DoesNotParticipateInWarning()
    {
        var tracker = new LowBatteryAlertTracker();

        var evaluation = tracker.Evaluate("device", Battery(left: 50, right: 60, @case: 1));

        Assert.Equal(LowBatterySystemAction.None, evaluation.SystemAction);
        Assert.False(evaluation.ShowInAppToast);
    }

    [Fact]
    public void Evaluate_LowEarbudCharging_CancelsExistingNotification()
    {
        var tracker = new LowBatteryAlertTracker();
        Assert.Equal(LowBatterySystemAction.Show,
            tracker.Evaluate("device", Battery(left: 4, right: 70, @case: 50)).SystemAction);

        var evaluation = tracker.Evaluate("device", Battery(left: 4, right: 70, @case: 50, leftCharging: true));

        Assert.Equal(LowBatterySystemAction.Cancel, evaluation.SystemAction);
    }

    [Fact]
    public void Evaluate_RecoveryAboveFive_CancelsAndAllowsFutureWarning()
    {
        var tracker = new LowBatteryAlertTracker();
        tracker.Evaluate("device", Battery(left: 5, right: 70, @case: 50));

        Assert.Equal(LowBatterySystemAction.Cancel,
            tracker.Evaluate("device", Battery(left: 6, right: 70, @case: 50)).SystemAction);
        Assert.Equal(LowBatterySystemAction.Show,
            tracker.Evaluate("device", Battery(left: 5, right: 70, @case: 50)).SystemAction);
    }

    [Fact]
    public void Evaluate_SixToTen_ShowsInAppToastOnlyOnceUntilDisconnect()
    {
        var tracker = new LowBatteryAlertTracker();
        var state = Battery(left: 10, right: 80, @case: 50);

        Assert.True(tracker.Evaluate("device", state).ShowInAppToast);
        Assert.False(tracker.Evaluate("device", state).ShowInAppToast);
        tracker.Reset("device");
        Assert.True(tracker.Evaluate("device", state).ShowInAppToast);
    }

    [Fact]
    public void Evaluate_UnavailableEarbud_DoesNotChangeExistingState()
    {
        var tracker = new LowBatteryAlertTracker();
        tracker.Evaluate("device", Battery(left: 5, right: 70, @case: 50));
        var unavailable = new EarbudBatteryState(new(null, false), new(70, false), new(1, false));

        Assert.Equal(LowBatteryEvaluation.NoChange, tracker.Evaluate("device", unavailable));
        Assert.Equal(LowBatterySystemAction.Cancel,
            tracker.Evaluate("device", Battery(left: 30, right: 70, @case: 50)).SystemAction);
    }

    [Fact]
    public void Reset_ReturnsWhetherSystemNotificationWasActive()
    {
        var tracker = new LowBatteryAlertTracker();
        tracker.Evaluate("device", Battery(left: 3, right: 50, @case: 50));

        Assert.True(tracker.Reset("device"));
        Assert.False(tracker.Reset("device"));
    }

    private static EarbudBatteryState Battery(
        int left,
        int right,
        int @case,
        bool leftCharging = false,
        bool rightCharging = false) =>
        new(new(left, leftCharging), new(right, rightCharging), new(@case, false));
}
