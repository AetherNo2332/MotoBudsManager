namespace MotoBudsManager.Core.Models;

public enum TransportKind
{
    Rfcomm,
    BleGatt,
}

public enum EvidenceLevel
{
    Inferred,
    StaticAnalysis,
    HardwareVerified,
}

public sealed record MotoBudsDevice(
    string DeviceId,
    string Name,
    ulong BluetoothAddress,
    string ModelId,
    byte ColorId,
    byte Capability,
    TransportKind Transport,
    EvidenceLevel Evidence,
    int? BatteryPercent = null)
{
    public string MaskedAddress
    {
        get
        {
            var value = BluetoothAddress.ToString("X12");
            return $"{value[..2]}:**:**:**:{value[8..10]}:{value[10..12]}";
        }
    }

    public bool IsXt2443ControlTarget =>
        ModelId.Equals("XT2443-1", StringComparison.OrdinalIgnoreCase) &&
        Transport == TransportKind.Rfcomm;

    public bool IsStableAncTarget => IsXt2443ControlTarget;
}
