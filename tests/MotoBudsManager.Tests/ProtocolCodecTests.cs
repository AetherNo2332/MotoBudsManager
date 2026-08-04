using MotoBudsManager.Core.Protocol;

namespace MotoBudsManager.Tests;

public sealed class ProtocolCodecTests
{
    [Fact]
    public void BuildPdu_AncOff_MatchesHardwareCapture()
    {
        var actual = ProtocolCodec.BuildPdu(0x0201, PduType.CommandAck, 0, 0, [0x00, 0x00]);

        Assert.Equal(Hex("02 01 80 00 02 00 00 00 00 00"), actual);
    }

    [Fact]
    public void BuildPdu_AncOn_MatchesHardwareCapture()
    {
        var actual = ProtocolCodec.BuildPdu(0x0201, PduType.CommandAck, 0, 0, [0x01, 0x01]);

        Assert.Equal(Hex("02 01 80 00 02 00 00 00 01 01"), actual);
    }

    [Fact]
    public void ParsePdu_UsesMixedEndianFields()
    {
        var message = ProtocolCodec.ParsePdu(Hex("02 00 20 00 02 00 01 00 01 01"));

        Assert.Equal((ushort)0x0200, message.Opcode);
        Assert.Equal(PduType.ResponseNoAck, message.Type);
        Assert.Equal((ushort)1, message.Sequence);
        Assert.Equal(new byte[] { 1, 1 }, message.Payload);
    }

    internal static byte[] Hex(string value) =>
        value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(item => Convert.ToByte(item, 16))
            .ToArray();
}
