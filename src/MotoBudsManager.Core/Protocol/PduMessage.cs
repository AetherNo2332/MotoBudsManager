namespace MotoBudsManager.Core.Protocol;

public enum PduType : byte
{
    CommandNoAck = 0x00,
    ResponseNoAck = 0x20,
    NotificationNoAck = 0x40,
    CommandAck = 0x80,
    ResponseAck = 0xA0,
    NotificationAck = 0xC0,
}

public sealed record PduMessage(
    ushort Opcode,
    PduType Type,
    byte Result,
    ushort Sequence,
    byte[] Payload);
