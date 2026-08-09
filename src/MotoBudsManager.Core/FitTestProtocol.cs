namespace MotoBudsManager.Core;

public enum FitTestEarStatus
{
    Unknown,
    Passed,
    Failed,
}

public sealed record FitTestResult(
    byte RawLeft,
    byte RawRight,
    FitTestEarStatus Left,
    FitTestEarStatus Right);

public static class FitTestProtocol
{
    public const ushort StatusChangedOpcode = 0x0401;

    public static bool TryDecode(ReadOnlySpan<byte> payload, out FitTestResult result)
    {
        if (payload.Length < 2)
        {
            result = null!;
            return false;
        }

        result = new(
            payload[0],
            payload[1],
            DecodeStatus(payload[0]),
            DecodeStatus(payload[1]));
        return true;
    }

    private static FitTestEarStatus DecodeStatus(byte value) => value switch
    {
        0 => FitTestEarStatus.Failed,
        1 => FitTestEarStatus.Passed,
        _ => FitTestEarStatus.Unknown,
    };
}
