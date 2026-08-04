using System.Buffers.Binary;

namespace MotoBudsManager.Core.Protocol;

public static class ProtocolCodec
{
    public const int HeaderLength = 8;

    public static byte[] BuildPdu(ushort opcode, PduType type, byte result, ushort sequence,
        ReadOnlySpan<byte> payload)
    {
        if (payload.Length > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(payload));
        }

        var bytes = new byte[HeaderLength + payload.Length];
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(0, 2), opcode);
        bytes[2] = (byte)type;
        bytes[3] = result;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4, 2), (ushort)payload.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6, 2), sequence);
        payload.CopyTo(bytes.AsSpan(HeaderLength));
        return bytes;
    }

    public static PduMessage ParsePdu(ReadOnlySpan<byte> bytes)
    {
        if (!TryParsePdu(bytes, out var message))
        {
            throw new InvalidDataException("The Moto Buds PDU is malformed.");
        }

        return message;
    }

    public static bool TryParsePdu(ReadOnlySpan<byte> bytes, out PduMessage message)
    {
        message = null!;
        if (bytes.Length < HeaderLength)
        {
            return false;
        }

        var payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(4, 2));
        if (bytes.Length != HeaderLength + payloadLength)
        {
            return false;
        }

        message = new PduMessage(
            BinaryPrimitives.ReadUInt16BigEndian(bytes[..2]),
            (PduType)bytes[2],
            bytes[3],
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(6, 2)),
            bytes[HeaderLength..].ToArray());
        return true;
    }
}
