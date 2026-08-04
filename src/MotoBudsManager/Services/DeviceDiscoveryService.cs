using MotoBudsManager.Core.Models;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;

namespace MotoBudsManager.Services;

public sealed class DeviceDiscoveryService(DiagnosticLogService log, bool isTestMode = false)
{
    private readonly bool _isTestMode = isTestMode;
    public static readonly Guid ControlServiceUuid = Guid.Parse("fc9d9fe0-4899-11ee-be56-0242ac120002");
    public static readonly Guid LegacyGattServiceUuid = Guid.Parse("00009fe0-4899-11ee-be56-0242ac120002");
    private static readonly Guid BatteryServiceUuid = Guid.Parse("0000180f-0000-1000-8000-00805f9b34fb");
    private static readonly Guid BatteryLevelUuid = Guid.Parse("00002a19-0000-1000-8000-00805f9b34fb");

    public async Task<IReadOnlyList<MotoBudsDevice>> DiscoverPairedAsync(
        CancellationToken cancellationToken = default)
    {
#if DEBUG
        if (_isTestMode)
        {
            log.Write("Returning DEBUG fake XT2443-1 and unverified devices.");
            return
            [
                new MotoBudsDevice(
                    "debug-fake-rfcomm",
                    "moto buds (test)",
                    0x001122334455,
                    "XT2443-1",
                    3,
                    0x10,
                    TransportKind.Rfcomm,
                    EvidenceLevel.HardwareVerified,
                    85),
                new MotoBudsDevice(
                    "debug-fake-unverified",
                    "moto buds plus (test)",
                    0x001122334466,
                    "XT2641-1",
                    1,
                    0x10,
                    TransportKind.Rfcomm,
                    EvidenceLevel.StaticAnalysis,
                    72),
            ];
        }
#endif
        var devices = new Dictionary<ulong, MotoBudsDevice>();
        await DiscoverRfcommAsync(devices, cancellationToken).ConfigureAwait(false);
        await DiscoverGattAsync(devices, cancellationToken).ConfigureAwait(false);
        return devices.Values
            .OrderByDescending(device => device.IsXt2443ControlTarget)
            .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private async Task DiscoverRfcommAsync(Dictionary<ulong, MotoBudsDevice> devices,
        CancellationToken cancellationToken)
    {
        var selector = RfcommDeviceService.GetDeviceSelector(RfcommServiceId.FromUuid(ControlServiceUuid));
        var serviceInfos = await DeviceInformation.FindAllAsync(selector).AsTask(cancellationToken).ConfigureAwait(false);
        foreach (var info in serviceInfos)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RfcommDeviceService? controlService = null;
            try
            {
                controlService = await RfcommDeviceService.FromIdAsync(info.Id).AsTask(cancellationToken)
                    .ConfigureAwait(false);
                if (controlService?.Device is not { } bluetoothDevice)
                {
                    continue;
                }

                var modelId = "Unknown";
                byte colorId = 0;
                var services = await bluetoothDevice.GetRfcommServicesAsync(BluetoothCacheMode.Uncached)
                    .AsTask(cancellationToken).ConfigureAwait(false);
                if (services.Error == BluetoothError.Success)
                {
                    foreach (var service in services.Services)
                    {
                        if (ModelCatalog.TryParseModelUuid(service.ServiceId.Uuid, out var parsedModel, out var parsedColor))
                        {
                            modelId = parsedModel;
                            colorId = parsedColor;
                            break;
                        }
                    }
                }

                if (!LooksLikeMotoBuds(bluetoothDevice.Name, modelId))
                {
                    continue;
                }

                var battery = await TryReadBatteryAsync(bluetoothDevice.BluetoothAddress, cancellationToken)
                    .ConfigureAwait(false);
                var evidence = modelId.Equals("XT2443-1", StringComparison.OrdinalIgnoreCase)
                    ? EvidenceLevel.HardwareVerified
                    : EvidenceLevel.StaticAnalysis;
                devices[bluetoothDevice.BluetoothAddress] = new MotoBudsDevice(
                    info.Id,
                    string.IsNullOrWhiteSpace(bluetoothDevice.Name) ? info.Name : bluetoothDevice.Name,
                    bluetoothDevice.BluetoothAddress,
                    modelId,
                    colorId,
                    0x10,
                    TransportKind.Rfcomm,
                    evidence,
                    battery);
                log.Write($"Discovered paired RFCOMM device {bluetoothDevice.Name} ({FormatAddress(bluetoothDevice.BluetoothAddress)}), model {modelId}.");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                log.Write($"RFCOMM discovery skipped a device: {exception.Message}");
            }
            finally
            {
                controlService?.Dispose();
            }
        }
    }

    private async Task DiscoverGattAsync(Dictionary<ulong, MotoBudsDevice> devices,
        CancellationToken cancellationToken)
    {
        var selector = BluetoothLEDevice.GetDeviceSelectorFromPairingState(true);
        var infos = await DeviceInformation.FindAllAsync(selector).AsTask(cancellationToken).ConfigureAwait(false);
        foreach (var info in infos)
        {
            cancellationToken.ThrowIfCancellationRequested();
            BluetoothLEDevice? bleDevice = null;
            try
            {
                bleDevice = await BluetoothLEDevice.FromIdAsync(info.Id).AsTask(cancellationToken).ConfigureAwait(false);
                if (bleDevice is null || devices.ContainsKey(bleDevice.BluetoothAddress))
                {
                    continue;
                }

                var allServices = await bleDevice.GetGattServicesAsync(BluetoothCacheMode.Cached)
                    .AsTask(cancellationToken).ConfigureAwait(false);
                if (allServices.Status != GattCommunicationStatus.Success)
                {
                    continue;
                }

                var modelId = "Unknown";
                byte colorId = 0;
                var hasControlService = false;
                foreach (var service in allServices.Services)
                {
                    hasControlService |= service.Uuid == ControlServiceUuid || service.Uuid == LegacyGattServiceUuid;
                    if (ModelCatalog.TryParseModelUuid(service.Uuid, out var parsedModel, out var parsedColor))
                    {
                        modelId = parsedModel;
                        colorId = parsedColor;
                    }
                }

                if (!hasControlService || !LooksLikeMotoBuds(bleDevice.Name, modelId))
                {
                    continue;
                }

                var battery = await TryReadBatteryAsync(bleDevice, cancellationToken).ConfigureAwait(false);
                devices[bleDevice.BluetoothAddress] = new MotoBudsDevice(
                    info.Id,
                    string.IsNullOrWhiteSpace(bleDevice.Name) ? info.Name : bleDevice.Name,
                    bleDevice.BluetoothAddress,
                    modelId,
                    colorId,
                    0x11,
                    TransportKind.BleGatt,
                    EvidenceLevel.StaticAnalysis,
                    battery);
                log.Write($"Discovered paired BLE device {bleDevice.Name} ({FormatAddress(bleDevice.BluetoothAddress)}), model {modelId}.");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                log.Write($"GATT discovery skipped a device: {exception.Message}");
            }
            finally
            {
                bleDevice?.Dispose();
            }
        }
    }

    private static async Task<int?> TryReadBatteryAsync(ulong address, CancellationToken cancellationToken)
    {
        using var device = await BluetoothLEDevice.FromBluetoothAddressAsync(address).AsTask(cancellationToken)
            .ConfigureAwait(false);
        return device is null ? null : await TryReadBatteryAsync(device, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int?> TryReadBatteryAsync(BluetoothLEDevice device,
        CancellationToken cancellationToken)
    {
        var services = await device.GetGattServicesForUuidAsync(BatteryServiceUuid, BluetoothCacheMode.Cached)
            .AsTask(cancellationToken).ConfigureAwait(false);
        if (services.Status != GattCommunicationStatus.Success || services.Services.Count == 0)
        {
            return null;
        }

        var characteristics = await services.Services[0]
            .GetCharacteristicsForUuidAsync(BatteryLevelUuid, BluetoothCacheMode.Cached)
            .AsTask(cancellationToken).ConfigureAwait(false);
        if (characteristics.Status != GattCommunicationStatus.Success || characteristics.Characteristics.Count == 0)
        {
            return null;
        }

        var value = await characteristics.Characteristics[0].ReadValueAsync(BluetoothCacheMode.Uncached)
            .AsTask(cancellationToken).ConfigureAwait(false);
        if (value.Status != GattCommunicationStatus.Success || value.Value.Length == 0)
        {
            return null;
        }

        using var reader = DataReader.FromBuffer(value.Value);
        return reader.ReadByte();
    }

    private static bool LooksLikeMotoBuds(string? name, string modelId) =>
        !modelId.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
        (!string.IsNullOrWhiteSpace(name) &&
         (name.Contains("moto", StringComparison.OrdinalIgnoreCase) ||
          name.Contains("buds", StringComparison.OrdinalIgnoreCase)));

    private static string FormatAddress(ulong address)
    {
        var value = address.ToString("X12");
        return string.Join(':', Enumerable.Range(0, 6).Select(index => value.Substring(index * 2, 2)));
    }
}
