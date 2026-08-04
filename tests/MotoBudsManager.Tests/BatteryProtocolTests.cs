using MotoBudsManager.Core;

namespace MotoBudsManager.Tests;

public sealed class BatteryProtocolTests
{
    [Fact]
    public void Decode_UsesLeftRightCaseWireOrderAndChargingBit()
    {
        var state = BatteryProtocol.Decode([0x14, 0x97, 0x36]);

        Assert.Equal(20, state.Left.Percent);
        Assert.False(state.Left.IsCharging);
        Assert.Equal(23, state.Right.Percent);
        Assert.True(state.Right.IsCharging);
        Assert.Equal(54, state.Case.Percent);
        Assert.False(state.Case.IsCharging);
    }

    [Fact]
    public void Decode_FfRemainsUnavailable()
    {
        var state = BatteryProtocol.Decode([0xFF, 0x01, 0x82]);

        Assert.Null(state.Left.Percent);
        Assert.False(state.Left.IsCharging);
        Assert.Equal(1, state.Right.Percent);
        Assert.Equal(2, state.Case.Percent);
        Assert.True(state.Case.IsCharging);
    }

    [Fact]
    public void Decode_ClampsFirmwarePercentToOneHundred()
    {
        var state = BatteryProtocol.Decode([0x7F, 0xFE, 0x64]);

        Assert.Equal(100, state.Left.Percent);
        Assert.Equal(100, state.Right.Percent);
        Assert.True(state.Right.IsCharging);
    }

    [Fact]
    public void TryDecode_ShortPayload_IsRejected()
    {
        Assert.False(BatteryProtocol.TryDecode([0x10, 0x20], out _));
    }

    [Fact]
    public void Cache_ReturnsLatestStateAndClearsOnDisconnect()
    {
        var cache = new BatteryStateCache();
        var state = BatteryProtocol.Decode([0x14, 0x17, 0x36]);

        cache.Set("device", state);
        Assert.True(cache.TryGet("device", out var cached));
        Assert.Same(state, cached);
        cache.Remove("device");
        Assert.False(cache.TryGet("device", out _));
    }
}
