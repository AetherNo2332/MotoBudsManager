using System.Globalization;
using MotoBudsManager.Core.Models;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace MotoBudsManager.Services;

public interface IEarbudConnectionSource
{
    Task<IReadOnlyList<MotoBudsDevice>> GetConnectedAsync(CancellationToken cancellationToken);
}

public sealed class WindowsEarbudConnectionSource(DeviceDiscoveryService discovery) : IEarbudConnectionSource
{
    private IReadOnlyList<MotoBudsDevice> _known = [];
    private HashSet<ulong> _examined = [];
    private DateTimeOffset _nextDiscovery;

    public async Task<IReadOnlyList<MotoBudsDevice>> GetConnectedAsync(CancellationToken cancellationToken)
    {
        var properties = new[] { "System.Devices.Aep.DeviceAddress", "System.Devices.Aep.IsConnected" };
        var classic = await DeviceInformation.FindAllAsync(
            BluetoothDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected),
            properties, DeviceInformationKind.AssociationEndpoint).AsTask(cancellationToken).ConfigureAwait(false);
        var ble = await DeviceInformation.FindAllAsync(
            BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected),
            properties, DeviceInformationKind.AssociationEndpoint).AsTask(cancellationToken).ConfigureAwait(false);
        var addresses = classic.Concat(ble).Where(info => info.Pairing.IsPaired)
            .Select(info => info.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out var address) ? ParseAddress(address as string) : null)
            .Where(address => address.HasValue).Select(address => address!.Value).ToHashSet();
        _examined.IntersectWith(addresses);
        if (addresses.Except(_known.Select(device => device.BluetoothAddress)).Except(_examined).Any() &&
            DateTimeOffset.UtcNow >= _nextDiscovery)
        {
            _nextDiscovery = DateTimeOffset.UtcNow.AddSeconds(30);
            _known = await discovery.DiscoverPairedAsync(cancellationToken).ConfigureAwait(false);
            // Resolve an endpoint once per Windows connection, not through repeated SDP requests every two seconds.
            if (_known.Count > 0) { _examined.UnionWith(addresses); }
        }
        return _known.Where(device => addresses.Contains(device.BluetoothAddress)).ToArray();
    }

    private static ulong? ParseAddress(string? value) =>
        ulong.TryParse(value?.Replace(":", string.Empty).Replace("-", string.Empty),
            NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var address) ? address : null;
}

#if DEBUG
internal sealed class DebugEarbudConnectionSource(DeviceDiscoveryService discovery, MotoBudsSessionService session) : IEarbudConnectionSource
{
    private IReadOnlyList<MotoBudsDevice>? _devices;

    public async Task<IReadOnlyList<MotoBudsDevice>> GetConnectedAsync(CancellationToken cancellationToken)
    {
        if (Environment.GetCommandLineArgs().Contains("--test-auto-monitor", StringComparer.OrdinalIgnoreCase))
        {
            _devices ??= await discovery.DiscoverPairedAsync(cancellationToken).ConfigureAwait(false);
            return _devices.Take(1).ToArray();
        }
        return session.IsConnected && session.Device is { } device ? [device] : [];
    }
}
#endif
