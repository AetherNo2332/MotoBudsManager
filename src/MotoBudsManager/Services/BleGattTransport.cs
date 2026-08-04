using MotoBudsManager.Core.Models;
using MotoBudsManager.Core.Protocol;
using MotoBudsManager.Core.Transport;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Security.Cryptography;

namespace MotoBudsManager.Services;

public sealed class BleGattTransport(MotoBudsDevice device, DiagnosticLogService log) : IMotoBudsTransport
{
    private static readonly Guid NewWriteUuid = Guid.Parse("fc9d9ff0-4899-11ee-be56-0242ac120002");
    private static readonly Guid NewNotifyUuid = Guid.Parse("fc9d9ff1-4899-11ee-be56-0242ac120002");
    private static readonly Guid OldWriteUuid = Guid.Parse("00009ff0-4899-11ee-be56-0242ac120002");
    private static readonly Guid OldNotifyUuid = Guid.Parse("00009ff1-4899-11ee-be56-0242ac120002");
    private BluetoothLEDevice? _bleDevice;
    private GattDeviceService? _service;
    private GattCharacteristic? _writeCharacteristic;
    private GattCharacteristic? _notifyCharacteristic;
    private int _disconnectSignaled;

    public event EventHandler<PduReceivedEventArgs>? PduReceived;

    public event EventHandler? Disconnected;

    public TransportKind Kind => TransportKind.BleGatt;

    public bool IsConnected { get; private set; }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        _bleDevice = await BluetoothLEDevice.FromIdAsync(device.DeviceId).AsTask(cancellationToken)
            .ConfigureAwait(false) ?? throw new InvalidOperationException("The paired BLE device is unavailable.");
        _bleDevice.ConnectionStatusChanged += OnConnectionStatusChanged;

        var serviceResult = await _bleDevice
            .GetGattServicesForUuidAsync(DeviceDiscoveryService.ControlServiceUuid, BluetoothCacheMode.Uncached)
            .AsTask(cancellationToken).ConfigureAwait(false);
        var isLegacy = false;
        if (serviceResult.Status != GattCommunicationStatus.Success || serviceResult.Services.Count == 0)
        {
            serviceResult = await _bleDevice
                .GetGattServicesForUuidAsync(DeviceDiscoveryService.LegacyGattServiceUuid, BluetoothCacheMode.Uncached)
                .AsTask(cancellationToken).ConfigureAwait(false);
            isLegacy = true;
        }

        if (serviceResult.Status != GattCommunicationStatus.Success || serviceResult.Services.Count == 0)
        {
            throw new InvalidOperationException($"Moto Buds GATT service discovery failed: {serviceResult.Status}.");
        }

        _service = serviceResult.Services[0];
        _writeCharacteristic = await GetCharacteristicAsync(_service, isLegacy ? OldWriteUuid : NewWriteUuid,
            cancellationToken).ConfigureAwait(false);
        _notifyCharacteristic = await GetCharacteristicAsync(_service, isLegacy ? OldNotifyUuid : NewNotifyUuid,
            cancellationToken).ConfigureAwait(false);
        _notifyCharacteristic.ValueChanged += OnValueChanged;
        var notifyStatus = await _notifyCharacteristic
            .WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify)
            .AsTask(cancellationToken).ConfigureAwait(false);
        if (notifyStatus != GattCommunicationStatus.Success)
        {
            throw new InvalidOperationException($"Enabling Moto Buds GATT notifications failed: {notifyStatus}.");
        }

        _disconnectSignaled = 0;
        IsConnected = true;
        log.Write($"GATT connected to {device.Name} ({device.MaskedAddress}).");
    }

    public async Task SendPduAsync(ReadOnlyMemory<byte> pdu, CancellationToken cancellationToken)
    {
        if (!IsConnected || _writeCharacteristic is null)
        {
            throw new InvalidOperationException("GATT is not connected.");
        }

        var result = await _writeCharacteristic
            .WriteValueWithResultAsync(CryptographicBuffer.CreateFromByteArray(pdu.ToArray()), GattWriteOption.WriteWithResponse)
            .AsTask(cancellationToken).ConfigureAwait(false);
        if (result.Status != GattCommunicationStatus.Success)
        {
            throw new IOException($"GATT write failed: {result.Status}, protocol error {result.ProtocolError}.");
        }

        var message = ProtocolCodec.ParsePdu(pdu.Span);
        log.Write($"TX GATT opcode=0x{message.Opcode:X4} sequence={message.Sequence} bytes={pdu.Length}.");
    }

    public async Task DisconnectAsync()
    {
        if (_notifyCharacteristic is not null)
        {
            _notifyCharacteristic.ValueChanged -= OnValueChanged;
            try
            {
                await _notifyCharacteristic
                    .WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.None);
            }
            catch
            {
            }
        }

        SignalDisconnected();
        if (_bleDevice is not null)
        {
            _bleDevice.ConnectionStatusChanged -= OnConnectionStatusChanged;
        }
        _service?.Dispose();
        _bleDevice?.Dispose();
        _service = null;
        _bleDevice = null;
        log.Write($"GATT disconnected from {device.Name}.");
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync().ConfigureAwait(false);

    private static async Task<GattCharacteristic> GetCharacteristicAsync(GattDeviceService service, Guid uuid,
        CancellationToken cancellationToken)
    {
        var result = await service.GetCharacteristicsForUuidAsync(uuid, BluetoothCacheMode.Uncached)
            .AsTask(cancellationToken).ConfigureAwait(false);
        if (result.Status != GattCommunicationStatus.Success || result.Characteristics.Count == 0)
        {
            throw new InvalidOperationException($"Required GATT characteristic {uuid} is unavailable.");
        }

        return result.Characteristics[0];
    }

    private void OnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
    {
        CryptographicBuffer.CopyToByteArray(args.CharacteristicValue, out var bytes);
        if (!ProtocolCodec.TryParsePdu(bytes, out var message))
        {
            log.Write($"Rejected malformed GATT notification ({bytes.Length} bytes).");
            return;
        }

        log.Write($"RX GATT opcode=0x{message.Opcode:X4} sequence={message.Sequence} result=0x{message.Result:X2}.");
        PduReceived?.Invoke(this, new(message));
    }

    private void OnConnectionStatusChanged(BluetoothLEDevice sender, object args)
    {
        if (sender.ConnectionStatus == BluetoothConnectionStatus.Disconnected)
        {
            log.Write($"GATT remote endpoint disconnected from {device.Name}.");
            SignalDisconnected();
        }
    }

    private void SignalDisconnected()
    {
        IsConnected = false;
        if (Interlocked.Exchange(ref _disconnectSignaled, 1) == 0)
        {
            Disconnected?.Invoke(this, EventArgs.Empty);
        }
    }
}
