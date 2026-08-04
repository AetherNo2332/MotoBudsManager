namespace MotoBudsManager.Core;

[Flags]
public enum BatteryComponentKind
{
    None = 0,
    Left = 1,
    Right = 2,
    Case = 4,
}

public sealed record LowBatteryAlert(BatteryComponentKind Components, int LowestPercent);

public sealed class LowBatteryAlertTracker(int thresholdPercent = 20)
{
    private readonly Dictionary<string, BatteryComponentKind> _activeAlerts = [];

    public LowBatteryAlert? Evaluate(string deviceKey, EarbudBatteryState state)
    {
        var lowComponents = GetLowComponents(state);
        _activeAlerts.TryGetValue(deviceKey, out var previouslyAlerted);
        _activeAlerts[deviceKey] = lowComponents;

        var newlyLow = lowComponents & ~previouslyAlerted;
        if (newlyLow == BatteryComponentKind.None)
        {
            return null;
        }

        var percentages = new List<int>(3);
        AddPercent(percentages, newlyLow, BatteryComponentKind.Left, state.Left);
        AddPercent(percentages, newlyLow, BatteryComponentKind.Right, state.Right);
        AddPercent(percentages, newlyLow, BatteryComponentKind.Case, state.Case);
        return new(newlyLow, percentages.Min());
    }

    private BatteryComponentKind GetLowComponents(EarbudBatteryState state)
    {
        var result = BatteryComponentKind.None;
        result |= IsLow(state.Left) ? BatteryComponentKind.Left : BatteryComponentKind.None;
        result |= IsLow(state.Right) ? BatteryComponentKind.Right : BatteryComponentKind.None;
        result |= IsLow(state.Case) ? BatteryComponentKind.Case : BatteryComponentKind.None;
        return result;
    }

    private bool IsLow(BatteryComponentState component) =>
        component.Percent is { } percent && percent < thresholdPercent && !component.IsCharging;

    private static void AddPercent(
        ICollection<int> percentages,
        BatteryComponentKind selected,
        BatteryComponentKind component,
        BatteryComponentState state)
    {
        if ((selected & component) != 0 && state.Percent is { } percent)
        {
            percentages.Add(percent);
        }
    }
}
