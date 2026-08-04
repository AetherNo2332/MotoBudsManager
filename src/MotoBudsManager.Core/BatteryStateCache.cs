namespace MotoBudsManager.Core;

public sealed class BatteryStateCache
{
    private readonly object _sync = new();
    private readonly Dictionary<string, EarbudBatteryState> _states = [];

    public bool TryGet(string deviceKey, out EarbudBatteryState state)
    {
        lock (_sync)
        {
            return _states.TryGetValue(deviceKey, out state!);
        }
    }

    public void Set(string deviceKey, EarbudBatteryState state)
    {
        lock (_sync)
        {
            _states[deviceKey] = state;
        }
    }

    public void Remove(string deviceKey)
    {
        lock (_sync)
        {
            _states.Remove(deviceKey);
        }
    }
}
