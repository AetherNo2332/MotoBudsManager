using MotoBudsManager.Core.Protocol;

namespace MotoBudsManager.Core.Transport;

public sealed class RequestDispatcher : IAsyncDisposable
{
    private readonly IMotoBudsTransport _transport;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly object _pendingLock = new();
    private readonly TimeSpan _timeout;
    private PendingRequest? _pending;
    private ushort _nextSequence;

    public event EventHandler<PduReceivedEventArgs>? MessageReceived;

    public RequestDispatcher(IMotoBudsTransport transport, TimeSpan? timeout = null)
    {
        _transport = transport;
        _timeout = timeout ?? TimeSpan.FromSeconds(5);
        _transport.PduReceived += OnPduReceived;
        _transport.Disconnected += OnDisconnected;
    }

    public ushort NextSequenceForTesting => _nextSequence;

    public void SetNextSequenceForTesting(ushort value) => _nextSequence = value;

    public async Task<PduMessage> SendAsync(ushort opcode, ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var sequence = _nextSequence++;
            var completion = new TaskCompletionSource<PduMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_pendingLock)
            {
                _pending = new PendingRequest(opcode, sequence, completion);
            }

            try
            {
                var pdu = ProtocolCodec.BuildPdu(opcode, PduType.CommandAck, 0, sequence, payload.Span);
                await _transport.SendPduAsync(pdu, cancellationToken).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(_timeout);
                try
                {
                    return await completion.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException($"Moto Buds command 0x{opcode:X4} timed out after {_timeout.TotalSeconds:0.#} seconds.");
                }
            }
            finally
            {
                lock (_pendingLock)
                {
                    _pending = null;
                }
            }
        }
        finally
        {
            _sendGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _transport.PduReceived -= OnPduReceived;
        _transport.Disconnected -= OnDisconnected;
        _sendGate.Dispose();
        await _transport.DisposeAsync().ConfigureAwait(false);
    }

    private void OnPduReceived(object? sender, PduReceivedEventArgs args)
    {
        lock (_pendingLock)
        {
            if (_pending is { } pending &&
                pending.Opcode == args.Message.Opcode &&
                pending.Sequence == args.Message.Sequence &&
                args.Message.Type is PduType.ResponseNoAck or PduType.ResponseAck)
            {
                pending.Completion.TrySetResult(args.Message);
            }
        }

        MessageReceived?.Invoke(this, args);
    }

    private void OnDisconnected(object? sender, EventArgs args)
    {
        lock (_pendingLock)
        {
            _pending?.Completion.TrySetException(
                new IOException("The Bluetooth connection was lost while waiting for a device response."));
        }
    }

    private sealed record PendingRequest(
        ushort Opcode,
        ushort Sequence,
        TaskCompletionSource<PduMessage> Completion);
}
