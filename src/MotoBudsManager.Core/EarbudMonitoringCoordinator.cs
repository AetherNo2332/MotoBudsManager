using MotoBudsManager.Core.Models;
using System.Collections.Concurrent;

namespace MotoBudsManager.Core;

public interface IEarbudMonitoringSession
{
    MotoBudsDevice? Device { get; }
    object? SessionIdentity { get; }
    bool IsConnected { get; }
    Task<bool> TryConnectForMonitoringAsync(MotoBudsDevice device, CancellationToken cancellationToken);
    Task<AncState> ReadAncStateAsync(CancellationToken cancellationToken);
    Task DisconnectForMonitoringAsync();
}

public sealed record AncObservation(MotoBudsDevice Device, AncState State, bool ModeChanged);

public sealed class EarbudMonitoringCoordinator(IEarbudMonitoringSession session)
{
    private readonly SemaphoreSlim _pollGate = new(1, 1);
    private readonly ConcurrentDictionary<ulong, bool> _suppressed = new();
    private readonly Dictionary<ulong, DateTimeOffset> _retryAfter = new();
    private HashSet<ulong> _connected = [];
    private object? _ancSession;
    private byte? _lastMode;

    public event EventHandler<MotoBudsDevice>? WindowsConnected;
    public event EventHandler<AncObservation>? AncObserved;
    public void SuppressUntilWindowsReconnect(ulong address) => _suppressed[address] = true;

    public async Task PollAsync(IReadOnlyList<MotoBudsDevice> connectedDevices, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (!await _pollGate.WaitAsync(0, cancellationToken).ConfigureAwait(false)) { return; }
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var compatible = connectedDevices.Where(device => ProductCatalog.IsKnownModel(device.ModelId))
                .DistinctBy(device => device.BluetoothAddress).ToArray();
            var current = compatible.Select(device => device.BluetoothAddress).ToHashSet();
            foreach (var address in _connected.Except(current))
            {
                _suppressed.TryRemove(address, out _);
                _retryAfter.Remove(address);
            }
            var newlyConnected = compatible.Where(device => !_connected.Contains(device.BluetoothAddress)).ToArray();
            _connected = current;
            foreach (var device in newlyConnected) { WindowsConnected?.Invoke(this, device); }

            if (session.Device is { } active && !current.Contains(active.BluetoothAddress))
            {
                await session.DisconnectForMonitoringAsync().ConfigureAwait(false);
            }
            if (!session.IsConnected)
            {
                var candidate = compatible.FirstOrDefault(device => !_suppressed.ContainsKey(device.BluetoothAddress) &&
                    (!_retryAfter.TryGetValue(device.BluetoothAddress, out var retry) || now >= retry));
                if (candidate is not null)
                {
                    // Back off even after a successful connect, so unstable SPP links do not reconnect every tick.
                    _retryAfter[candidate.BluetoothAddress] = now.AddSeconds(30);
                    await session.TryConnectForMonitoringAsync(candidate, cancellationToken).ConfigureAwait(false);
                }
            }

            var identity = session.SessionIdentity;
            if (!ReferenceEquals(identity, _ancSession))
            {
                _ancSession = identity;
                _lastMode = null;
            }
            if (!session.IsConnected || identity is null || session.Device is not { } deviceToRead ||
                !current.Contains(deviceToRead.BluetoothAddress) || !ModelCatalog.Supports(deviceToRead.ModelId, ExperimentalFeature.Anc))
            {
                return;
            }
            var state = await session.ReadAncStateAsync(cancellationToken).ConfigureAwait(false);
            if (!ReferenceEquals(identity, session.SessionIdentity) || state.Mode > 2) { return; }
            var changed = _lastMode.HasValue && _lastMode.Value != state.Mode;
            _lastMode = state.Mode;
            AncObserved?.Invoke(this, new(deviceToRead, state, changed));
        }
        finally { _pollGate.Release(); }
    }
}
