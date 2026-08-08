#if DEBUG
using MotoBudsManager.Core.Models;
using MotoBudsManager.Core.Protocol;
using MotoBudsManager.Core.Transport;

namespace MotoBudsManager.Services;

internal sealed class DebugFakeTransport(DiagnosticLogService log) : IMotoBudsTransport
{
    private AncStateValue _anc = new(1, 1);
    private bool _adaptiveHearing = true;
    private bool _hiRes = true;
    private bool _gamingMode;
    private byte _spatialAudio;
    private bool _automaticVolume;
    private bool _bassBoost = true;
    private bool _volumeBoost;
    private bool _inEarDetection = true;
    private bool _dualConnection;

    public event EventHandler<PduReceivedEventArgs>? PduReceived;

    public event EventHandler? Disconnected;

    public TransportKind Kind => TransportKind.Rfcomm;

    public bool IsConnected { get; private set; }

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        IsConnected = true;
        log.Write("DEBUG fake transport connected.");
        return Task.CompletedTask;
    }

    public Task SendPduAsync(ReadOnlyMemory<byte> pdu, CancellationToken cancellationToken)
    {
        var request = ProtocolCodec.ParsePdu(pdu.Span);
        byte[] payload = request.Opcode switch
        {
            0x0005 => [0x94, 0x93, 0x36],
            0x0200 => [_anc.Mode, _anc.Subtype],
            0x0202 => [(byte)(_adaptiveHearing ? 1 : 0), 0x01],
            0x030A => _spatialAudio switch
            {
                0 => [0x00, 0x00],
                1 => [0x01, 0x00],
                _ => [0x01, 0x01],
            },
            0x030C => [(byte)(_hiRes ? 1 : 0)],
            0x030E => [(byte)(_gamingMode ? 1 : 0)],
            0x0313 => [(byte)(_volumeBoost ? 1 : 0)],
            0x0316 => [(byte)(_automaticVolume ? 1 : 0)],
            0x031C => [(byte)(_bassBoost ? 1 : 0)],
            0x0402 => [(byte)(_inEarDetection ? 1 : 0)],
            0x0406 => [0x01, (byte)(_dualConnection ? 1 : 0)],
            _ => [],
        };
        byte result = 0;
        switch (request.Opcode)
        {
            case 0x0201 when request.Payload.Length >= 2:
                _anc = new(request.Payload[0], request.Payload[1]);
                break;
            case 0x0203 when request.Payload.Length >= 1:
                _adaptiveHearing = request.Payload[0] != 0;
                break;
            case 0x030D when request.Payload.Length >= 1:
                _hiRes = request.Payload[0] != 0;
                break;
            case 0x030B when request.Payload.Length >= 2:
                _spatialAudio = request.Payload[0] == 0 ? (byte)0 : request.Payload[1] == 0 ? (byte)1 : (byte)2;
                break;
            case 0x030F when request.Payload.Length >= 1:
                _gamingMode = request.Payload[0] != 0;
                break;
            case 0x0314 when request.Payload.Length >= 1:
                _volumeBoost = request.Payload[0] != 0;
                break;
            case 0x0317 when request.Payload.Length >= 1:
                _automaticVolume = request.Payload[0] != 0;
                break;
            case 0x031D when request.Payload.Length >= 1:
                _bassBoost = request.Payload[0] != 0;
                break;
            case 0x0403 when request.Payload.Length >= 1:
                _inEarDetection = request.Payload[0] != 0;
                break;
            case 0x0407 when request.Payload.Length >= 2:
                _dualConnection = request.Payload[1] != 0;
                break;
            case 0x0407:
                result = 2;
                break;
        }

        var response = new PduMessage(request.Opcode, PduType.ResponseNoAck, result, request.Sequence, payload);
        PduReceived?.Invoke(this, new(response));
        log.Write($"DEBUG fake response opcode=0x{request.Opcode:X4} sequence={request.Sequence}.");
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        IsConnected = false;
        Disconnected?.Invoke(this, EventArgs.Empty);
        log.Write("DEBUG fake transport disconnected.");
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private sealed record AncStateValue(byte Mode, byte Subtype);
}
#endif
