namespace MotoBudsManager.Core;

public enum LowBatterySystemAction
{
    None,
    Show,
    Cancel,
}

public sealed record LowBatteryEvaluation(
    LowBatterySystemAction SystemAction,
    bool ShowInAppToast,
    int? LowestPercent)
{
    public static LowBatteryEvaluation NoChange { get; } = new(LowBatterySystemAction.None, false, null);
}

public sealed class LowBatteryAlertTracker
{
    private readonly Dictionary<string, AlertState> _states = [];

    public LowBatteryEvaluation Evaluate(string deviceKey, EarbudBatteryState battery)
    {
        if (battery.Left.Percent is not { } left || battery.Right.Percent is not { } right)
        {
            return LowBatteryEvaluation.NoChange;
        }

        if (!_states.TryGetValue(deviceKey, out var state))
        {
            state = new AlertState();
            _states[deviceKey] = state;
        }

        var lowest = Math.Min(left, right);
        var needsSystemWarning = lowest <= 5 &&
            (NeedsWarning(left, battery.Left.IsCharging) ||
             NeedsWarning(right, battery.Right.IsCharging));

        var systemAction = LowBatterySystemAction.None;
        if (needsSystemWarning && !state.SystemNotificationShown)
        {
            state.SystemNotificationShown = true;
            systemAction = LowBatterySystemAction.Show;
        }
        else if (!needsSystemWarning && state.SystemNotificationShown)
        {
            state.SystemNotificationShown = false;
            systemAction = LowBatterySystemAction.Cancel;
        }

        var showToast = lowest is >= 6 and <= 10 && !state.InAppToastShown;
        if (showToast)
        {
            state.InAppToastShown = true;
        }

        return new(systemAction, showToast, lowest);
    }

    public bool Reset(string deviceKey)
    {
        if (!_states.Remove(deviceKey, out var state))
        {
            return false;
        }

        return state.SystemNotificationShown;
    }

    private static bool NeedsWarning(int percent, bool isCharging) =>
        percent is >= 0 and <= 5 && !isCharging;

    private sealed class AlertState
    {
        public bool SystemNotificationShown { get; set; }

        public bool InAppToastShown { get; set; }
    }
}
