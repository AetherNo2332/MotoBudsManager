using MotoBudsManager.Core.Models;
using MotoBudsManager.Core.Protocol;
using MotoBudsManager.Core.Transport;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace MotoBudsManager.Services;

public sealed class RfcommTransport(MotoBudsDevice device, DiagnosticLogService log) : IMotoBudsTransport
{
    private readonly SppFrameParser _parser = new();
    private readonly CancellationTokenSource _receiveCancellation = new();
    private RfcommDeviceService? _service;
    private StreamSocket? _socket;
    private DataReader? _reader;
    private DataWriter? _writer;
    private Task? _receiveTask;

    public event EventHandler<PduReceivedEventArgs>? PduReceived;

    public TransportKind Kind => TransportKind.Rfcomm;

    public bool IsConnected { get; private set; }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (IsConnected)
        {
            return;
        }

        _service = await RfcommDeviceService.FromIdAsync(device.DeviceId).AsTask(cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidOperationException("The paired RFCOMM service is unavailable.");
        _socket = new StreamSocket();
        await _socket.ConnectAsync(
                _service.ConnectionHostName,
                _service.ConnectionServiceName,
                SocketProtectionLevel.BluetoothEncryptionWithAuthentication)
            .AsTask(cancellationToken).ConfigureAwait(false);
        _reader = new DataReader(_socket.InputStream) { InputStreamOptions = InputStreamOptions.Partial };
        _writer = new DataWriter(_socket.OutputStream);
        IsConnected = true;
        log.Write($"RFCOMM connected to {device.Name} ({device.MaskedAddress}).");
        _receiveTask = ReceiveLoopAsync(_receiveCancellation.Token);
    }

    public async Task SendPduAsync(ReadOnlyMemory<byte> pdu, CancellationToken cancellationToken)
    {
        if (!IsConnected || _writer is null)
        {
            throw new InvalidOperationException("RFCOMM is not connected.");
        }

        var frame = SppFrameCodec.BuildFrame(pdu.Span);
        _writer.WriteBytes(frame);
        await _writer.StoreAsync().AsTask(cancellationToken).ConfigureAwait(false);
        await _writer.FlushAsync().AsTask(cancellationToken).ConfigureAwait(false);
        var message = ProtocolCodec.ParsePdu(pdu.Span);
        log.Write($"TX SPP opcode=0x{message.Opcode:X4} sequence={message.Sequence} bytes={frame.Length}.");
    }

    public async Task DisconnectAsync()
    {
        if (!IsConnected)
        {
            return;
        }

        IsConnected = false;
        _receiveCancellation.Cancel();
        _socket?.Dispose();
        if (_receiveTask is not null)
        {
            try
            {
                await _receiveTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        log.Write($"RFCOMM disconnected from {device.Name}.");
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
        _reader?.Dispose();
        _writer?.Dispose();
        _socket?.Dispose();
        _service?.Dispose();
        _receiveCancellation.Dispose();
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _reader is not null)
        {
            var loaded = await _reader.LoadAsync(1024).AsTask(cancellationToken).ConfigureAwait(false);
            if (loaded == 0)
            {
                IsConnected = false;
                log.Write("RFCOMM remote endpoint closed the connection.");
                return;
            }

            var bytes = new byte[loaded];
            _reader.ReadBytes(bytes);
            foreach (var message in _parser.Feed(bytes))
            {
                log.Write($"RX SPP opcode=0x{message.Opcode:X4} sequence={message.Sequence} result=0x{message.Result:X2}.");
                PduReceived?.Invoke(this, new(message));
            }
        }
    }
}
