using MotoBudsManager.Core;

namespace MotoBudsManager.Tests;

public sealed class FitTestProtocolTests
{
    [Theory]
    [InlineData(0x01, 0x01, FitTestEarStatus.Passed, FitTestEarStatus.Passed)]
    [InlineData(0x00, 0x01, FitTestEarStatus.Failed, FitTestEarStatus.Passed)]
    [InlineData(0x01, 0x00, FitTestEarStatus.Passed, FitTestEarStatus.Failed)]
    [InlineData(0x00, 0x00, FitTestEarStatus.Failed, FitTestEarStatus.Failed)]
    public void TryDecode_KnownFirmwareResults_MapsEachEar(
        byte rawLeft,
        byte rawRight,
        FitTestEarStatus expectedLeft,
        FitTestEarStatus expectedRight)
    {
        var parsed = FitTestProtocol.TryDecode([rawLeft, rawRight], out var result);

        Assert.True(parsed);
        Assert.Equal(expectedLeft, result.Left);
        Assert.Equal(expectedRight, result.Right);
        Assert.Equal(rawLeft, result.RawLeft);
        Assert.Equal(rawRight, result.RawRight);
    }

    [Fact]
    public void TryDecode_UnknownValue_PreservesRawByte()
    {
        var parsed = FitTestProtocol.TryDecode([0x7F, 0x01], out var result);

        Assert.True(parsed);
        Assert.Equal(FitTestEarStatus.Unknown, result.Left);
        Assert.Equal((byte)0x7F, result.RawLeft);
        Assert.Equal(FitTestEarStatus.Passed, result.Right);
    }

    [Theory]
    [InlineData()]
    [InlineData(0x01)]
    public void TryDecode_TruncatedPayload_IsRejected(params byte[] payload)
    {
        Assert.False(FitTestProtocol.TryDecode(payload, out _));
    }
}
