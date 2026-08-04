using MotoBudsManager.Core;
using MotoBudsManager.Core.Models;
using MotoBudsManager.Core.Transport;

namespace MotoBudsManager.Services;

public sealed class MotoBudsSessionService(DiagnosticLogService log, bool isTestMode = false) : IAsyncDisposable
{
    private readonly bool _isTestMode = isTestMode;
    private IMotoBudsTransport? _transport;
    private RequestDispatcher? _dispatcher;

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
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await _transport.ConnectAsync(timeout.Token).ConfigureAwait(false);
        _dispatcher = new RequestDispatcher(_transport, TimeSpan.FromSeconds(5));
        Device = device;
        Client = new MotoBudsClient(_dispatcher, device);
    }

    public async Task DisconnectAsync()
    {
        if (Client is { HasTransientActions: true } && Device?.IsXt2443ControlTarget == true)
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
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync().ConfigureAwait(false);
}
