using MotoBudsManager.Core;
using MotoBudsManager.Core.Models;
using MotoBudsManager.Core.Transport;

namespace MotoBudsManager.Services;

public enum BatteryUpdateSource
{
    InitialSnapshot,
    FirmwareNotification,
    Cache,
}

public sealed class BatteryStateChangedEventArgs(
    MotoBudsDevice device,
    EarbudBatteryState state,
    BatteryUpdateSource source) : EventArgs
{
    public MotoBudsDevice Device { get; } = device;

    public EarbudBatteryState State { get; } = state;

    public BatteryUpdateSource Source { get; } = source;
}

public enum SessionDisconnectReason
{
    Requested,
    TransportLost,
}

public sealed class SessionDisconnectedEventArgs(
    MotoBudsDevice device,
    SessionDisconnectReason reason) : EventArgs
{
    public MotoBudsDevice Device { get; } = device;

    public SessionDisconnectReason Reason { get; } = reason;
}

public sealed class MotoBudsSessionService(DiagnosticLogService log, bool isTestMode = false) : IAsyncDisposable
{
    private readonly bool _isTestMode = isTestMode;
    private readonly BatteryStateCache _batteryCache = new();
    private readonly SemaphoreSlim _batteryQueryGate = new(1, 1);
    private IMotoBudsTransport? _transport;
    private RequestDispatcher? _dispatcher;
    private int _disconnectRaised;

    public event EventHandler<BatteryStateChangedEventArgs>? BatteryStateChanged;

    public event EventHandler<SessionDisconnectedEventArgs>? Disconnected;

    public MotoBudsDevice? Device { get; private set; }

    public MotoBudsClient? Client { get; private set; }

    public bool IsConnected => _transport?.IsConnected == true;

    public async Task ConnectAsync(MotoBudsDevice device, CancellationToken cancellationToken = default)
    {
        await DisconnectAsync().ConfigureAwait(false);
#if DEBUG
        if (_isTestMode)
        {
            _transport = new DebugFakeTransport(log);
        }
        else
#endif
        {
            _transport = device.Transport == TransportKind.Rfcomm
                ? new RfcommTransport(device, log)
                : new BleGattTransport(device, log);
        }

        Device = device;
        Client = null;
        Volatile.Write(ref _disconnectRaised, 0);
        _transport.Disconnected += OnTransportDisconnected;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            await _transport.ConnectAsync(timeout.Token).ConfigureAwait(false);
            EnsureTransportConnected();

            _dispatcher = new RequestDispatcher(_transport, TimeSpan.FromSeconds(5));
            _dispatcher.MessageReceived += OnMessageReceived;
            Client = new MotoBudsClient(_dispatcher, device);

            try
            {
                await QueryBatterySnapshotAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                log.Write($"Initial battery snapshot failed for {device.ModelId}: {exception.Message}");
            }

            EnsureTransportConnected();
        }
        catch
        {
            try
            {
                await DisconnectAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupException)
            {
                log.Write($"Connection cleanup failed for {device.ModelId}: {cleanupException.Message}");
            }

            throw;
        }
    }

    public async Task<EarbudBatteryState?> GetBatteryStateAsync(CancellationToken cancellationToken = default)
    {
        var device = Device;
        if (device is null || !IsConnected)
        {
            return null;
        }

        var key = DeviceKey(device);
        if (_batteryCache.TryGet(key, out var cached))
        {
            return cached;
        }

        return await QueryBatterySnapshotAsync(cancellationToken).ConfigureAwait(false);
    }

    public bool TryGetCachedBatteryState(out EarbudBatteryState state)
    {
        var device = Device;
        if (device is not null && _batteryCache.TryGet(DeviceKey(device), out state!))
        {
            return true;
        }

        state = null!;
        return false;
    }

    public async Task DisconnectAsync()
    {
        var disconnectedDevice = Device;
        if (Client is { HasTransientActions: true } && disconnectedDevice is not null)
        {
            using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            try
            {
                await Client.StopTransientActionsAsync(stopTimeout.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                log.Write($"Transient XT2443 actions could not be stopped cleanly: {exception.Message}");
            }
            catch (OperationCanceledException)
            {
                log.Write("Timed out while stopping transient XT2443 actions during disconnect.");
            }
        }

        Client = null;
        Device = null;
        if (disconnectedDevice is not null)
        {
            _batteryCache.Remove(DeviceKey(disconnectedDevice));
        }

        if (_dispatcher is not null)
        {
            _dispatcher.MessageReceived -= OnMessageReceived;
        }
        if (_transport is not null)
        {
            _transport.Disconnected -= OnTransportDisconnected;
        }

        if (_dispatcher is not null)
        {
            await _dispatcher.DisposeAsync().ConfigureAwait(false);
            _dispatcher = null;
            _transport = null;
        }
        else if (_transport is not null)
        {
            await _transport.DisposeAsync().ConfigureAwait(false);
            _transport = null;
        }

        if (disconnectedDevice is not null)
        {
            RaiseDisconnected(disconnectedDevice, SessionDisconnectReason.Requested);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _batteryQueryGate.Dispose();
    }

    private async Task<EarbudBatteryState?> QueryBatterySnapshotAsync(CancellationToken cancellationToken)
    {
        await _batteryQueryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var device = Device;
            var client = Client;
            if (device is null || client is null || !IsConnected || !ProductCatalog.IsKnownModel(device.ModelId))
            {
                return null;
            }

            var key = DeviceKey(device);
            if (_batteryCache.TryGet(key, out var cached))
            {
                return cached;
            }

            var state = await client.GetBatteryStateAsync(cancellationToken).ConfigureAwait(false);
            UpdateBattery(device, state, BatteryUpdateSource.InitialSnapshot);
            return state;
        }
        finally
        {
            _batteryQueryGate.Release();
        }
    }

    private void OnMessageReceived(object? sender, PduReceivedEventArgs args)
    {
        var device = Device;
        if (device is null || args.Message.Opcode != BatteryProtocol.BatteryLevelChangedOpcode)
        {
            return;
        }

        if (!BatteryProtocol.TryDecode(args.Message.Payload, out var state))
        {
            log.Write($"Rejected malformed battery notification for {device.ModelId}.");
            return;
        }

        UpdateBattery(device, state, BatteryUpdateSource.FirmwareNotification);
    }

    private void UpdateBattery(
        MotoBudsDevice device,
        EarbudBatteryState state,
        BatteryUpdateSource source)
    {
        _batteryCache.Set(DeviceKey(device), state);
        log.Write($"Battery state updated from {source}: left={Format(state.Left)}, right={Format(state.Right)}, case={Format(state.Case)}.");
        BatteryStateChanged?.Invoke(this, new(device, state, source));
    }

    private void OnTransportDisconnected(object? sender, EventArgs args)
    {
        if (!ReferenceEquals(sender, _transport))
        {
            return;
        }

        var device = Device;
        if (device is null)
        {
            return;
        }

        Client = null;
        Device = null;
        _batteryCache.Remove(DeviceKey(device));
        RaiseDisconnected(device, SessionDisconnectReason.TransportLost);
    }

    private void RaiseDisconnected(MotoBudsDevice device, SessionDisconnectReason reason)
    {
        if (Interlocked.Exchange(ref _disconnectRaised, 1) == 0)
        {
            Disconnected?.Invoke(this, new(device, reason));
        }
    }

    private void EnsureTransportConnected()
    {
        if (!IsConnected || Device is null)
        {
            throw new IOException("The Bluetooth device disconnected while the session was being initialized.");
        }
    }

    private static string DeviceKey(MotoBudsDevice device) => device.BluetoothAddress.ToString("X12");

    private static string Format(BatteryComponentState component) => component.Percent is { } percent
        ? $"{percent}%{(component.IsCharging ? "+" : string.Empty)}"
        : "unavailable";
}
