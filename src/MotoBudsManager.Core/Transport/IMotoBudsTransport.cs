using MotoBudsManager.Core.Models;
using MotoBudsManager.Core.Protocol;

namespace MotoBudsManager.Core.Transport;

public sealed class PduReceivedEventArgs(PduMessage message) : EventArgs
{
    public PduMessage Message { get; } = message;
}

public interface IMotoBudsTransport : IAsyncDisposable
{
    event EventHandler<PduReceivedEventArgs>? PduReceived;

    event EventHandler? Disconnected;

    TransportKind Kind { get; }

    bool IsConnected { get; }

    Task ConnectAsync(CancellationToken cancellationToken);

    Task SendPduAsync(ReadOnlyMemory<byte> pdu, CancellationToken cancellationToken);

    Task DisconnectAsync();
}
