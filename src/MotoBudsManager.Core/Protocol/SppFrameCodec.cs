using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace MotoBudsManager.Core.Protocol;

public static class SppFrameCodec
{
    public static ReadOnlySpan<byte> Header => "HEAD"u8;
    public static ReadOnlySpan<byte> Tail => "TAIL"u8;

    public static byte[] BuildFrame(ReadOnlySpan<byte> pdu)
    {
        if (pdu.Length is < ProtocolCodec.HeaderLength or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(pdu));
        }

        var frame = new byte[pdu.Length + 14];
        Header.CopyTo(frame);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(4, 2), (ushort)pdu.Length);
        pdu.CopyTo(frame.AsSpan(6));
        var crcOffset = 6 + pdu.Length;
        var crc = Crc32.Compute(frame.AsSpan(0, crcOffset));
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(crcOffset, 4), crc);
        Tail.CopyTo(frame.AsSpan(crcOffset + 4));
        return frame;
    }
}

public static class Crc32
{
    public static uint Compute(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}

public sealed class SppFrameParser
{
    private const int MaxPduLength = 4096;
    private readonly List<byte> _buffer = [];

    public int RejectedFrames { get; private set; }

    public IReadOnlyList<PduMessage> Feed(ReadOnlySpan<byte> bytes)
    {
        _buffer.AddRange(bytes.ToArray());
        var messages = new List<PduMessage>();

        while (true)
        {
            var headerIndex = FindHeader();
            if (headerIndex < 0)
            {
                KeepPossibleHeaderPrefix();
                break;
            }

            if (headerIndex > 0)
            {
                _buffer.RemoveRange(0, headerIndex);
            }

            if (_buffer.Count < 6)
            {
                break;
            }

            var pduLength = _buffer[4] | (_buffer[5] << 8);
            if (pduLength is < ProtocolCodec.HeaderLength or > MaxPduLength)
            {
                RejectedFrames++;
                _buffer.RemoveAt(0);
                continue;
            }

            var totalLength = pduLength + 14;
            if (_buffer.Count < totalLength)
            {
                break;
            }

            var frame = CollectionsMarshal.AsSpan(_buffer)[..totalLength];
            var crcOffset = 6 + pduLength;
            if (!frame.Slice(crcOffset + 4, 4).SequenceEqual(SppFrameCodec.Tail))
            {
                RejectedFrames++;
                _buffer.RemoveAt(0);
                continue;
            }

            var expectedCrc = BinaryPrimitives.ReadUInt32LittleEndian(frame.Slice(crcOffset, 4));
            var actualCrc = Crc32.Compute(frame[..crcOffset]);
            if (expectedCrc != 0 && expectedCrc != actualCrc)
            {
                RejectedFrames++;
                _buffer.RemoveRange(0, totalLength);
                continue;
            }

            if (ProtocolCodec.TryParsePdu(frame.Slice(6, pduLength), out var message))
            {
                messages.Add(message);
            }
            else
            {
                RejectedFrames++;
            }

            _buffer.RemoveRange(0, totalLength);
        }

        return messages;
    }

    private int FindHeader()
    {
        var span = CollectionsMarshal.AsSpan(_buffer);
        for (var index = 0; index <= span.Length - SppFrameCodec.Header.Length; index++)
        {
            if (span.Slice(index, SppFrameCodec.Header.Length).SequenceEqual(SppFrameCodec.Header))
            {
                return index;
            }
        }

        return -1;
    }

    private void KeepPossibleHeaderPrefix()
    {
        var keep = Math.Min(3, _buffer.Count);
        if (_buffer.Count > keep)
        {
            _buffer.RemoveRange(0, _buffer.Count - keep);
        }

        while (_buffer.Count > 0 && !SppFrameCodec.Header[.._buffer.Count].SequenceEqual(CollectionsMarshal.AsSpan(_buffer)))
        {
            _buffer.RemoveAt(0);
        }
    }
}
