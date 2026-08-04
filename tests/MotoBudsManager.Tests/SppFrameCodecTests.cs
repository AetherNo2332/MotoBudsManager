using MotoBudsManager.Core.Protocol;

namespace MotoBudsManager.Tests;

public sealed class SppFrameCodecTests
{
    [Fact]
    public void BuildFrame_AncOff_MatchesHardwareCapture()
    {
        var pdu = ProtocolCodecTests.Hex("02 01 80 00 02 00 00 00 00 00");

        var actual = SppFrameCodec.BuildFrame(pdu);

        Assert.Equal(ProtocolCodecTests.Hex(
            "48 45 41 44 0A 00 02 01 80 00 02 00 00 00 00 00 62 02 62 0E 54 41 49 4C"), actual);
    }

    [Fact]
    public void Feed_FragmentedAndNoisyFrames_ReturnsAllMessages()
    {
        var first = SppFrameCodec.BuildFrame(ProtocolCodec.BuildPdu(0x0201, PduType.ResponseNoAck, 0, 7, []));
        var second = SppFrameCodec.BuildFrame(ProtocolCodec.BuildPdu(0x0200, PduType.ResponseNoAck, 0, 8, [1, 1]));
        var stream = new byte[] { 0x99, 0x98, 0x48, 0x45 }
            .Concat(first)
            .Concat(second)
            .ToArray();
        var parser = new SppFrameParser();

        var messages = new List<PduMessage>();
        messages.AddRange(parser.Feed(stream.AsSpan(0, 5)));
        messages.AddRange(parser.Feed(stream.AsSpan(5, 9)));
        messages.AddRange(parser.Feed(stream.AsSpan(14)));

        Assert.Equal(2, messages.Count);
        Assert.Equal((ushort)0x0201, messages[0].Opcode);
        Assert.Equal((ushort)0x0200, messages[1].Opcode);
    }

    [Fact]
    public void Feed_ZeroCrcFirmwareResponse_IsAccepted()
    {
        var response = ProtocolCodecTests.Hex(
            "48 45 41 44 08 00 02 01 20 00 00 00 00 00 00 00 00 00 54 41 49 4C");
        var parser = new SppFrameParser();

        var messages = parser.Feed(response);

        Assert.Single(messages);
        Assert.Equal((ushort)0x0201, messages[0].Opcode);
    }

    [Fact]
    public void Feed_InvalidNonZeroCrc_IsRejected()
    {
        var frame = SppFrameCodec.BuildFrame(ProtocolCodec.BuildPdu(0x0201, PduType.ResponseNoAck, 0, 0, []));
        frame[^8] ^= 0xFF;
        var parser = new SppFrameParser();

        var messages = parser.Feed(frame);

        Assert.Empty(messages);
        Assert.Equal(1, parser.RejectedFrames);
    }
}
