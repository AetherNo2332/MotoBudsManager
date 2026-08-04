using System.Globalization;

namespace MotoBudsManager.Core.Models;

public enum ExperimentalFeature
{
    Anc,
    SpatialAudio,
    HiRes,
    GamingMode,
    AutomaticVolume,
    BassBoost,
    VolumeBoost,
    InEarDetection,
    FindEarbuds,
}

public sealed record FeatureCommand(
    ExperimentalFeature Feature,
    ushort Opcode,
    byte[] Payload,
    EvidenceLevel Evidence);

public static class ModelCatalog
{
    private static readonly IReadOnlyDictionary<ExperimentalFeature, HashSet<string>> SupportedModels =
        new Dictionary<ExperimentalFeature, HashSet<string>>
        {
            [ExperimentalFeature.Anc] = Set("XT2441-1", "XT2443-1", "XT2641-1", "XT2643-1"),
            [ExperimentalFeature.SpatialAudio] = Set("XT2441-1", "XT2641-1"),
            [ExperimentalFeature.HiRes] = Set("XT2441-1", "XT2443-1", "XT2641-1", "XT2643-1"),
            [ExperimentalFeature.GamingMode] = Set("XT2441-1", "XT2443-1", "XT2545-1", "XT2641-1", "XT2643-1"),
            [ExperimentalFeature.AutomaticVolume] = Set("XT2545-1"),
            [ExperimentalFeature.BassBoost] = Set("XT2441-1", "XT2443-1", "XT2641-1", "XT2643-1"),
            [ExperimentalFeature.VolumeBoost] = Set("XT2441-1", "XT2443-1", "XT2643-1"),
            [ExperimentalFeature.InEarDetection] = Set("XT2441-1", "XT2443-1", "XT2545-1", "XT2641-1", "XT2643-1"),
            [ExperimentalFeature.FindEarbuds] = Set("XT2441-1", "XT2443-1", "XT2545-1", "XT2641-1", "XT2643-1"),
        };

    public static bool Supports(string modelId, ExperimentalFeature feature) =>
        SupportedModels.TryGetValue(feature, out var models) && models.Contains(modelId);

    public static FeatureCommand CreateCommand(string modelId, ExperimentalFeature feature, int value)
    {
        if (!Supports(modelId, feature))
        {
            throw new NotSupportedException($"{feature} is not whitelisted for {modelId}.");
        }

        return feature switch
        {
            ExperimentalFeature.Anc => new(feature, 0x0201, value switch
            {
                0 => [0x00, 0x00],
                1 => [0x01, 0x01],
                _ => throw InvalidValue(feature, value),
            }, EvidenceLevel.StaticAnalysis),
            ExperimentalFeature.SpatialAudio => new(feature, 0x030B, value switch
            {
                0 => [0x00, 0x00],
                1 => [0x01, 0x00],
                2 => [0x01, 0x01],
                _ => throw InvalidValue(feature, value),
            }, EvidenceLevel.StaticAnalysis),
            ExperimentalFeature.HiRes => Toggle(feature, 0x030D, value),
            ExperimentalFeature.GamingMode => Toggle(feature, 0x030F, value),
            ExperimentalFeature.AutomaticVolume => Toggle(feature, 0x0317, value),
            ExperimentalFeature.BassBoost => Toggle(feature, 0x031D, value),
            ExperimentalFeature.VolumeBoost => Toggle(feature, 0x0314, value),
            ExperimentalFeature.InEarDetection => Toggle(feature, 0x0403, value),
            ExperimentalFeature.FindEarbuds => new(feature, 0x0405, value switch
            {
                0 => [0x00, 0x00],
                1 => [0x01, 0x00],
                2 => [0x00, 0x01],
                3 => [0x01, 0x01],
                _ => throw InvalidValue(feature, value),
            }, EvidenceLevel.StaticAnalysis),
            _ => throw new ArgumentOutOfRangeException(nameof(feature)),
        };
    }

    public static bool TryParseModelUuid(Guid uuid, out string modelId, out byte colorId)
    {
        modelId = string.Empty;
        colorId = 0;
        var text = uuid.ToString("D", CultureInfo.InvariantCulture);
        if (!text.EndsWith("-4899-11ee-be56-0242ac120002", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var first = text[..8];
        if (first.Equals("fc9d9fe0", StringComparison.OrdinalIgnoreCase) ||
            !byte.TryParse(first.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var a) ||
            !byte.TryParse(first.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b) ||
            !byte.TryParse(first.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var suffix) ||
            !byte.TryParse(first.AsSpan(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out colorId))
        {
            return false;
        }

        modelId = $"XT{a}{b}-{suffix}";
        return true;
    }

    private static FeatureCommand Toggle(ExperimentalFeature feature, ushort opcode, int value)
    {
        if (value is not (0 or 1))
        {
            throw InvalidValue(feature, value);
        }

        return new(feature, opcode, [(byte)value], EvidenceLevel.StaticAnalysis);
    }

    private static ArgumentOutOfRangeException InvalidValue(ExperimentalFeature feature, int value) =>
        new(nameof(value), value, $"Value is not valid for {feature}.");

    private static HashSet<string> Set(params string[] values) =>
        new(values, StringComparer.OrdinalIgnoreCase);
}

public static class ManufacturerDataParser
{
    public const ushort MotorolaCompanyId = 0x0008;

    public static bool TryParse(ReadOnlySpan<byte> payload, out string modelId, out byte colorId,
        out byte capability, out ulong bluetoothAddress, out TransportKind transport)
    {
        modelId = string.Empty;
        colorId = 0;
        capability = 0;
        bluetoothAddress = 0;
        transport = TransportKind.Rfcomm;

        if (payload.Length != 11)
        {
            return false;
        }

        modelId = $"XT{payload[0]}{payload[1]}-{payload[2]}";
        colorId = payload[3];
        capability = payload[4];
        for (var index = 5; index < 11; index++)
        {
            bluetoothAddress = (bluetoothAddress << 8) | payload[index];
        }

        transport = capability == 0x11 ? TransportKind.BleGatt : TransportKind.Rfcomm;
        return true;
    }
}
