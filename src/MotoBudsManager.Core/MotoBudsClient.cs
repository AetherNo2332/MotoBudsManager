using MotoBudsManager.Core.Models;
using MotoBudsManager.Core.Protocol;
using MotoBudsManager.Core.Transport;

namespace MotoBudsManager.Core;

public sealed record AncState(byte Mode, byte Subtype)
{
    public bool IsStandardAncOn => Mode == 1 && Subtype == 1;
}

public enum AncModeValue : byte
{
    Off = 0,
    NoiseCancellation = 1,
    Transparency = 2,
}

public enum Xt2443ToggleFeature
{
    AdaptiveHearing,
    HiRes,
    GamingMode,
    VolumeBoost,
    InEarDetection,
}

public enum EarbudRingTarget
{
    Stop,
    Left,
    Right,
}

public sealed record BatteryComponentState(int? Percent, bool IsCharging);

public sealed record EarbudBatteryState(
    BatteryComponentState Left,
    BatteryComponentState Right,
    BatteryComponentState Case);

public sealed record DualConnectionState(bool IsSupported, bool IsEnabled);

public sealed record CommandResult(
    bool TransportSucceeded,
    bool AckReceived,
    bool? StateVerified,
    string? Error,
    PduMessage? Response = null)
{
    public bool Success => TransportSucceeded && AckReceived && StateVerified is not false && Error is null;
}

public sealed class MotoBudsClient(
    RequestDispatcher dispatcher,
    MotoBudsDevice device,
    TimeSpan? readbackDelay = null)
{
    private readonly TimeSpan _readbackDelay = readbackDelay ?? TimeSpan.FromMilliseconds(250);
    private bool _ringActive;
    private bool _fitTestActive;

    public bool HasTransientActions => _ringActive || _fitTestActive;

    public void CompleteFitTest() => _fitTestActive = false;

    public async Task<AncState> GetAncStateAsync(CancellationToken cancellationToken = default)
    {
        EnsureFeatureSupported(ExperimentalFeature.Anc);
        var response = await dispatcher.SendAsync(0x0200, ReadOnlyMemory<byte>.Empty, cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccessful(response);
        if (response.Payload.Length < 2)
        {
            throw new InvalidDataException("The ANC response did not contain mode and subtype bytes.");
        }

        return new AncState(response.Payload[0], response.Payload[1]);
    }

    public Task<CommandResult> SetAncOnAsync(CancellationToken cancellationToken = default) =>
        SetAncModeAsync(AncModeValue.NoiseCancellation, cancellationToken);

    public Task<CommandResult> SetAncOffAsync(CancellationToken cancellationToken = default) =>
        SetAncModeAsync(AncModeValue.Off, cancellationToken);

    public Task<CommandResult> SetAncModeAsync(
        AncModeValue mode,
        CancellationToken cancellationToken = default) =>
        SetAncAsync(mode switch
        {
            AncModeValue.Off => new AncState(0, 0),
            AncModeValue.NoiseCancellation => new AncState(1, 1),
            AncModeValue.Transparency => new AncState(2, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        }, cancellationToken);

    public async Task<EarbudBatteryState> GetBatteryStateAsync(CancellationToken cancellationToken = default)
    {
        EnsureKnownModel();
        var response = await dispatcher.SendAsync(BatteryProtocol.GetBatteryLevelOpcode, ReadOnlyMemory<byte>.Empty, cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccessful(response);
        return BatteryProtocol.Decode(response.Payload);
    }

    public Task<bool> GetXt2443ToggleAsync(
        Xt2443ToggleFeature feature,
        CancellationToken cancellationToken = default)
    {
        var mappedFeature = MapToggleFeature(feature);
        EnsureFeatureSupported(mappedFeature);
        return GetBooleanStateAsync(GetToggleOpcodes(feature).Get, cancellationToken);
    }

    public Task<CommandResult> SetXt2443ToggleAsync(
        Xt2443ToggleFeature feature,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        EnsureFeatureSupported(MapToggleFeature(feature));
        var opcodes = GetToggleOpcodes(feature);
        return SetVerifiedAsync(opcodes.Set, [(byte)(enabled ? 1 : 0)], opcodes.Get,
            payload => payload.Length >= 1 && (payload[0] != 0) == enabled,
            cancellationToken);
    }

    public async Task<DualConnectionState> GetDualConnectionStateAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureFeatureSupported(ExperimentalFeature.DualConnection);
        var response = await dispatcher.SendAsync(0x0406, ReadOnlyMemory<byte>.Empty, cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccessful(response);
        if (response.Payload.Length < 2)
        {
            throw new InvalidDataException("The dual-connection response did not contain support and state bytes.");
        }

        return new DualConnectionState(response.Payload[0] != 0, response.Payload[1] != 0);
    }

    public async Task<CommandResult> SetDualConnectionAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        EnsureFeatureSupported(ExperimentalFeature.DualConnection);
        try
        {
            var current = await GetDualConnectionStateAsync(cancellationToken).ConfigureAwait(false);
            if (!current.IsSupported)
            {
                return new(true, false, false, "The device reports that dual connection is unsupported.");
            }

            return await SetVerifiedAsync(0x0407, [0x01, (byte)(enabled ? 1 : 0)], 0x0406,
                payload => payload.Length >= 2 && payload[0] == 1 && (payload[1] != 0) == enabled,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new(false, false, false, exception.Message);
        }
    }

    public async Task<CommandResult> SetRingTargetAsync(
        EarbudRingTarget target,
        CancellationToken cancellationToken = default)
    {
        EnsureFeatureSupported(ExperimentalFeature.FindEarbuds);
        var result = await SendActionAsync(0x0405, target switch
        {
            EarbudRingTarget.Stop => [0x00, 0x00],
            EarbudRingTarget.Left => [0x01, 0x00],
            EarbudRingTarget.Right => [0x00, 0x01],
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        }, cancellationToken).ConfigureAwait(false);
        if (result.Success)
        {
            _ringActive = target != EarbudRingTarget.Stop;
        }

        return result;
    }

    public async Task<CommandResult> SetFitTestAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        EnsureFeatureSupported(ExperimentalFeature.FitTest);
        var result = await SendActionAsync(0x0400, [(byte)(enabled ? 1 : 0)], cancellationToken)
            .ConfigureAwait(false);
        if (result.Success)
        {
            _fitTestActive = enabled;
        }

        return result;
    }

    public async Task StopTransientActionsAsync(CancellationToken cancellationToken = default)
    {
        if (_ringActive)
        {
            await SetRingTargetAsync(EarbudRingTarget.Stop, cancellationToken).ConfigureAwait(false);
        }

        if (_fitTestActive)
        {
            await SetFitTestAsync(false, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<CommandResult> SetExperimentalFeatureAsync(
        ExperimentalFeature feature,
        int value,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var command = ModelCatalog.CreateCommand(device.ModelId, feature, value);
            var response = await dispatcher.SendAsync(command.Opcode, command.Payload, cancellationToken)
                .ConfigureAwait(false);
            if (response.Result != 0)
            {
                return new(true, true, null, $"Device rejected command with result 0x{response.Result:X2}.", response);
            }

            return new(true, true, null, null, response);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new(false, false, null, exception.Message);
        }
    }

    public async Task<int> GetFeatureStateAsync(
        ExperimentalFeature feature,
        CancellationToken cancellationToken = default)
    {
        EnsureFeatureSupported(feature);
        if (feature == ExperimentalFeature.Anc)
        {
            return (await GetAncStateAsync(cancellationToken).ConfigureAwait(false)).Mode;
        }

        var getOpcode = GetFeatureOpcodes(feature).Get;
        var response = await dispatcher.SendAsync(getOpcode, ReadOnlyMemory<byte>.Empty, cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccessful(response);
        if (response.Payload.Length < 1)
        {
            throw new InvalidDataException($"Response 0x{getOpcode:X4} did not contain a state byte.");
        }

        return feature == ExperimentalFeature.SpatialAudio
            ? response.Payload[0] == 0 ? 0 : response.Payload.Length > 1 && response.Payload[1] != 0 ? 2 : 1
            : response.Payload[0] != 0 ? 1 : 0;
    }

    public Task<CommandResult> SetFeatureAsync(
        ExperimentalFeature feature,
        int value,
        CancellationToken cancellationToken = default)
    {
        EnsureFeatureSupported(feature);
        if (feature == ExperimentalFeature.Anc)
        {
            return SetAncModeAsync((AncModeValue)value, cancellationToken);
        }

        var command = ModelCatalog.CreateCommand(device.ModelId, feature, value);
        var getOpcode = GetFeatureOpcodes(feature).Get;
        return SetVerifiedAsync(command.Opcode, command.Payload, getOpcode,
            payload => FeaturePayloadMatches(feature, value, payload), cancellationToken);
    }

    private async Task<CommandResult> SetAncAsync(AncState expected, CancellationToken cancellationToken)
    {
        EnsureFeatureSupported(ExperimentalFeature.Anc);
        try
        {
            var response = await dispatcher.SendAsync(0x0201, new byte[] { expected.Mode, expected.Subtype }, cancellationToken)
                .ConfigureAwait(false);
            if (response.Result != 0)
            {
                return new(true, true, false, $"Device rejected ANC command with result 0x{response.Result:X2}.", response);
            }

            AncState? actual = null;
            for (var attempt = 0; attempt < 3; attempt++)
            {
                actual = await GetAncStateAsync(cancellationToken).ConfigureAwait(false);
                var verified = actual.Mode == expected.Mode &&
                               (expected.Mode != 1 || actual.Subtype == expected.Subtype);
                if (verified)
                {
                    return new(true, true, true, null, response);
                }

                if (attempt < 2)
                {
                    await Task.Delay(_readbackDelay * (attempt + 1), cancellationToken).ConfigureAwait(false);
                }
            }

            return new(true, true, false,
                $"ANC read-back mismatch: mode={actual!.Mode}, subtype={actual.Subtype}.", response);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new(false, false, false, exception.Message);
        }
    }

    private async Task<bool> GetBooleanStateAsync(
        ushort opcode,
        CancellationToken cancellationToken)
    {
        var response = await dispatcher.SendAsync(opcode, ReadOnlyMemory<byte>.Empty, cancellationToken)
            .ConfigureAwait(false);
        EnsureSuccessful(response);
        if (response.Payload.Length < 1)
        {
            throw new InvalidDataException($"Response 0x{opcode:X4} did not contain a state byte.");
        }

        return response.Payload[0] != 0;
    }

    private async Task<CommandResult> SetVerifiedAsync(
        ushort setOpcode,
        byte[] payload,
        ushort getOpcode,
        Func<byte[], bool> isExpected,
        CancellationToken cancellationToken)
    {
        PduMessage? acknowledgement = null;
        try
        {
            acknowledgement = await dispatcher.SendAsync(setOpcode, payload, cancellationToken)
                .ConfigureAwait(false);
            if (acknowledgement.Result != 0)
            {
                return new(true, true, false,
                    $"Device rejected command with result 0x{acknowledgement.Result:X2}.", acknowledgement);
            }

            await Task.Delay(_readbackDelay, cancellationToken).ConfigureAwait(false);
            var readback = await dispatcher.SendAsync(getOpcode, ReadOnlyMemory<byte>.Empty, cancellationToken)
                .ConfigureAwait(false);
            EnsureSuccessful(readback);
            return isExpected(readback.Payload)
                ? new(true, true, true, null, acknowledgement)
                : new(true, true, false, $"State read-back for 0x{getOpcode:X4} did not match.", acknowledgement);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return acknowledgement is null
                ? new(false, false, false, exception.Message)
                : new(true, true, false, exception.Message, acknowledgement);
        }
    }

    private async Task<CommandResult> SendActionAsync(
        ushort opcode,
        byte[] payload,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await dispatcher.SendAsync(opcode, payload, cancellationToken).ConfigureAwait(false);
            return response.Result == 0
                ? new(true, true, null, null, response)
                : new(true, true, null, $"Device rejected command with result 0x{response.Result:X2}.", response);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new(false, false, null, exception.Message);
        }
    }

    private (ushort Get, ushort Set) GetToggleOpcodes(Xt2443ToggleFeature feature)
    {
        return feature switch
        {
            Xt2443ToggleFeature.AdaptiveHearing => (0x0202, 0x0203),
            Xt2443ToggleFeature.HiRes => (0x030C, 0x030D),
            Xt2443ToggleFeature.GamingMode => (0x030E, 0x030F),
            Xt2443ToggleFeature.VolumeBoost => (0x0313, 0x0314),
            Xt2443ToggleFeature.InEarDetection => (0x0402, 0x0403),
            _ => throw new ArgumentOutOfRangeException(nameof(feature)),
        };
    }

    private static (ushort Get, ushort Set) GetFeatureOpcodes(ExperimentalFeature feature) => feature switch
    {
        ExperimentalFeature.AdaptiveHearing => (0x0202, 0x0203),
        ExperimentalFeature.SpatialAudio => (0x030A, 0x030B),
        ExperimentalFeature.HiRes => (0x030C, 0x030D),
        ExperimentalFeature.GamingMode => (0x030E, 0x030F),
        ExperimentalFeature.VolumeBoost => (0x0313, 0x0314),
        ExperimentalFeature.AutomaticVolume => (0x0316, 0x0317),
        ExperimentalFeature.BassBoost => (0x031C, 0x031D),
        ExperimentalFeature.InEarDetection => (0x0402, 0x0403),
        _ => throw new NotSupportedException($"{feature} does not expose a reliable read-back state."),
    };

    private static bool FeaturePayloadMatches(ExperimentalFeature feature, int value, byte[] payload)
    {
        if (payload.Length < 1)
        {
            return false;
        }

        if (feature != ExperimentalFeature.SpatialAudio)
        {
            return (payload[0] != 0) == (value != 0);
        }

        return value switch
        {
            0 => payload[0] == 0,
            1 => payload[0] != 0 && (payload.Length < 2 || payload[1] == 0),
            2 => payload[0] != 0 && payload.Length > 1 && payload[1] != 0,
            _ => false,
        };
    }

    private static ExperimentalFeature MapToggleFeature(Xt2443ToggleFeature feature) => feature switch
    {
        Xt2443ToggleFeature.AdaptiveHearing => ExperimentalFeature.AdaptiveHearing,
        Xt2443ToggleFeature.HiRes => ExperimentalFeature.HiRes,
        Xt2443ToggleFeature.GamingMode => ExperimentalFeature.GamingMode,
        Xt2443ToggleFeature.VolumeBoost => ExperimentalFeature.VolumeBoost,
        Xt2443ToggleFeature.InEarDetection => ExperimentalFeature.InEarDetection,
        _ => throw new ArgumentOutOfRangeException(nameof(feature)),
    };

    private void EnsureFeatureSupported(ExperimentalFeature feature)
    {
        if (!ModelCatalog.Supports(device.ModelId, feature))
        {
            throw new NotSupportedException($"{feature} is not supported for {device.ModelId}.");
        }
    }

    private void EnsureKnownModel()
    {
        if (!ProductCatalog.IsKnownModel(device.ModelId))
        {
            throw new NotSupportedException($"Battery state is not supported for {device.ModelId}.");
        }
    }

    private static void EnsureSuccessful(PduMessage response)
    {
        if (response.Result != 0)
        {
            throw new InvalidOperationException($"Device returned result 0x{response.Result:X2}.");
        }
    }
}
