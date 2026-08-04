namespace MotoBudsManager.Core;

public static class BatteryProtocol
{
    public const ushort GetBatteryLevelOpcode = 0x0005;
    public const ushort BatteryLevelChangedOpcode = 0x0009;

    public static EarbudBatteryState Decode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 3)
        {
            throw new InvalidDataException("The battery payload did not contain left, right, and case bytes.");
        }

        return new EarbudBatteryState(
            DecodeComponent(payload[0]),
            DecodeComponent(payload[1]),
            DecodeComponent(payload[2]));
    }

    public static bool TryDecode(ReadOnlySpan<byte> payload, out EarbudBatteryState state)
    {
        if (payload.Length < 3)
        {
            state = null!;
            return false;
        }

        state = Decode(payload);
        return true;
    }

    private static BatteryComponentState DecodeComponent(byte raw) => raw == 0xFF
        ? new(null, false)
        : new(Math.Min(raw & 0x7F, 100), (raw & 0x80) != 0);
}
