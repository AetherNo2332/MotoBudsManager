using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MotoBudsManager.Core;
using MotoBudsManager.Core.Models;
using MotoBudsManager.Services;
using Windows.System;

namespace MotoBudsManager.ViewModels;

public partial class MainPageViewModel : ObservableObject
{
    private readonly DeviceDiscoveryService discovery;
    private readonly MotoBudsSessionService session;
    private readonly DiagnosticLogService diagnostics;
    private readonly LowBatteryNotificationService lowBatteryNotifications;

    public MainPageViewModel(
        DeviceDiscoveryService discovery,
        MotoBudsSessionService session,
        DiagnosticLogService diagnostics,
        LowBatteryNotificationService lowBatteryNotifications)
    {
        this.discovery = discovery;
        this.session = session;
        this.diagnostics = diagnostics;
        this.lowBatteryNotifications = lowBatteryNotifications;
        session.BatteryStateChanged += OnBatteryStateChanged;
        session.Disconnected += OnSessionDisconnected;
        lowBatteryNotifications.InAppLowBatteryRequested += OnInAppLowBatteryRequested;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConnect))]
    [NotifyPropertyChangedFor(nameof(DeviceName))]
    [NotifyPropertyChangedFor(nameof(ModelId))]
    [NotifyPropertyChangedFor(nameof(TransportText))]
    [NotifyPropertyChangedFor(nameof(BatteryText))]
    [NotifyPropertyChangedFor(nameof(AddressText))]
    [NotifyPropertyChangedFor(nameof(EvidenceText))]
    [NotifyPropertyChangedFor(nameof(ProductName))]
    [NotifyPropertyChangedFor(nameof(ProductImageSource))]
    [NotifyPropertyChangedFor(nameof(ProductModelAndColor))]
    [NotifyPropertyChangedFor(nameof(IsXt2443ControlsAvailable))]
    [NotifyPropertyChangedFor(nameof(IsStableAncAvailable))]
    [NotifyPropertyChangedFor(nameof(CanUseAnc))]
    [NotifyPropertyChangedFor(nameof(CanUseSpatialAudio))]
    [NotifyPropertyChangedFor(nameof(CanUseHiRes))]
    [NotifyPropertyChangedFor(nameof(CanUseGaming))]
    [NotifyPropertyChangedFor(nameof(CanUseAutomaticVolume))]
    [NotifyPropertyChangedFor(nameof(CanUseBassBoost))]
    [NotifyPropertyChangedFor(nameof(CanUseVolumeBoost))]
    [NotifyPropertyChangedFor(nameof(CanUseInEarDetection))]
    [NotifyPropertyChangedFor(nameof(CanUseFindEarbuds))]
    public partial MotoBudsDevice? SelectedDevice { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConnect))]
    [NotifyPropertyChangedFor(nameof(CanDisconnect))]
    [NotifyPropertyChangedFor(nameof(ConnectionStatusText))]
    [NotifyPropertyChangedFor(nameof(IsXt2443ControlsAvailable))]
    [NotifyPropertyChangedFor(nameof(IsStableAncAvailable))]
    [NotifyPropertyChangedFor(nameof(CanUseAnc))]
    [NotifyPropertyChangedFor(nameof(CanUseSpatialAudio))]
    [NotifyPropertyChangedFor(nameof(CanUseHiRes))]
    [NotifyPropertyChangedFor(nameof(CanUseGaming))]
    [NotifyPropertyChangedFor(nameof(CanUseAutomaticVolume))]
    [NotifyPropertyChangedFor(nameof(CanUseBassBoost))]
    [NotifyPropertyChangedFor(nameof(CanUseVolumeBoost))]
    [NotifyPropertyChangedFor(nameof(CanUseInEarDetection))]
    [NotifyPropertyChangedFor(nameof(CanUseFindEarbuds))]
    public partial bool IsConnected { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConnect))]
    [NotifyPropertyChangedFor(nameof(CanDisconnect))]
    [NotifyPropertyChangedFor(nameof(IsXt2443ControlsAvailable))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial bool StatusIsOpen { get; set; }

    [ObservableProperty]
    public partial bool StatusIsError { get; set; }

    [ObservableProperty]
    public partial string StatusTitle { get; set; } = LocalizationService.Get("StatusReady");

    [ObservableProperty]
    public partial string StatusMessage { get; set; } = LocalizationService.Get("StatusReadyMessage");

    [ObservableProperty]
    public partial string ExperimentalStatus { get; set; } = LocalizationService.Get("ExperimentalNoCommand");

    [ObservableProperty]
    public partial int AncMode { get; set; } = -1;

    [ObservableProperty]
    public partial bool AdaptiveHearingEnabled { get; set; }

    [ObservableProperty]
    public partial bool HiResEnabled { get; set; }

    [ObservableProperty]
    public partial bool GamingModeEnabled { get; set; }

    [ObservableProperty]
    public partial bool VolumeBoostEnabled { get; set; }

    [ObservableProperty]
    public partial bool InEarDetectionEnabled { get; set; }

    [ObservableProperty]
    public partial bool DualConnectionEnabled { get; set; }

    [ObservableProperty]
    public partial bool DualConnectionSupported { get; set; }

    [ObservableProperty]
    public partial bool IsFindActive { get; set; }

    [ObservableProperty]
    public partial bool IsFitTestActive { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BatterySummaryText))]
    public partial string LeftBatteryText { get; set; } = "—";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BatterySummaryText))]
    public partial string RightBatteryText { get; set; } = "—";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BatterySummaryText))]
    public partial string CaseBatteryText { get; set; } = "—";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BatterySummaryText))]
    public partial bool LeftBatteryCharging { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BatterySummaryText))]
    public partial bool RightBatteryCharging { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BatterySummaryText))]
    public partial bool CaseBatteryCharging { get; set; }

    public ObservableCollection<MotoBudsDevice> Devices { get; } = [];

    public ObservableCollection<DeviceCardViewModel> DeviceCards { get; } = [];

    public ObservableCollection<string> LogEntries => diagnostics.Entries;

    public bool HasDevices => Devices.Count > 0;

    public bool CanConnect => SelectedDevice is not null && !IsConnected && !IsBusy;

    public bool CanDisconnect => IsConnected && !IsBusy;

    public string DeviceName => SelectedDevice?.Name ?? LocalizationService.Get("NoDeviceSelected");

    public string ModelId => SelectedDevice?.ModelId ?? "—";

    public string TransportText => SelectedDevice?.Transport.ToString() ?? "—";

    public string BatteryText => SelectedDevice?.BatteryPercent is { } value ? $"{value}%" : LocalizationService.Get("Unavailable");

    public string AddressText => SelectedDevice?.MaskedAddress ?? "—";

    public string EvidenceText => SelectedDevice?.Evidence switch
    {
        EvidenceLevel.HardwareVerified => LocalizationService.Get("EvidenceHardware"),
        EvidenceLevel.StaticAnalysis => LocalizationService.Get("EvidenceStatic"),
        _ => LocalizationService.Get("EvidenceInferred"),
    };

    public string ProductName => SelectedDevice is null
        ? LocalizationService.Get("NoDeviceSelected")
        : ProductCatalog.Get(SelectedDevice.ModelId, SelectedDevice.ColorId).ProductName;

    public string ProductImageSource => SelectedDevice is null
        ? "ms-appx:///Assets/Products/default-tws.webp"
        : $"ms-appx:///Assets/Products/{ProductCatalog.Get(SelectedDevice.ModelId, SelectedDevice.ColorId).ImageFileName}";

    public string ProductModelAndColor => SelectedDevice is null
        ? "—"
        : $"{SelectedDevice.ModelId}  ·  {ProductCatalog.Get(SelectedDevice.ModelId, SelectedDevice.ColorId).ColorName}";

    public string ConnectionStatusText => IsConnected
        ? LocalizationService.Get("StatusConnected")
        : LocalizationService.Get("StatusDisconnected");

    public string BatterySummaryText => string.Format(
        LocalizationService.Get("BatterySummaryFormat"),
        FormatBatteryStatus(LeftBatteryText, LeftBatteryCharging),
        FormatBatteryStatus(RightBatteryText, RightBatteryCharging),
        FormatBatteryStatus(CaseBatteryText, CaseBatteryCharging));

    public string ChargingLabel => LocalizationService.Get("ChargingLabel");

    public bool IsStableAncAvailable => IsConnected && SelectedDevice?.IsStableAncTarget == true;

    public bool IsXt2443ControlsAvailable =>
        IsConnected && !IsBusy && SelectedDevice?.IsXt2443ControlTarget == true;

    public bool CanUseAnc => CanUse(ExperimentalFeature.Anc);
    public bool CanUseSpatialAudio => CanUse(ExperimentalFeature.SpatialAudio);
    public bool CanUseHiRes => CanUse(ExperimentalFeature.HiRes);
    public bool CanUseGaming => CanUse(ExperimentalFeature.GamingMode);
    public bool CanUseAutomaticVolume => CanUse(ExperimentalFeature.AutomaticVolume);
    public bool CanUseBassBoost => CanUse(ExperimentalFeature.BassBoost);
    public bool CanUseVolumeBoost => CanUse(ExperimentalFeature.VolumeBoost);
    public bool CanUseInEarDetection => CanUse(ExperimentalFeature.InEarDetection);
    public bool CanUseFindEarbuds => CanUse(ExperimentalFeature.FindEarbuds);

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        ShowStatus(LocalizationService.Get("StatusScanning"), LocalizationService.Get("StatusScanningMessage"));
        try
        {
            var found = await discovery.DiscoverPairedAsync();
            Devices.Clear();
            DeviceCards.Clear();
            for (var index = 0; index < found.Count; index++)
            {
                var device = found[index];
                Devices.Add(device);
                DeviceCards.Add(new DeviceCardViewModel(device, index));
            }

            OnPropertyChanged(nameof(HasDevices));
            SelectedDevice = Devices.FirstOrDefault();
            foreach (var card in DeviceCards.Where(card => card.Device.IsXt2443ControlTarget))
            {
                await RefreshHomeBatteryAsync(card);
            }
            ShowStatus(
                found.Count > 0 ? LocalizationService.Get("StatusDevicesFound") : LocalizationService.Get("StatusNoDevices"),
                found.Count > 0
                    ? string.Format(LocalizationService.Get("StatusDevicesFoundMessage"), found.Count)
                    : LocalizationService.Get("StatusNoDevicesMessage"));
        }
        catch (Exception exception)
        {
            ShowError(LocalizationService.Get("StatusScanFailed"), exception.Message);
            diagnostics.Write($"Discovery failed: {exception}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        IsBusy = true;
        ShowStatus(LocalizationService.Get("StatusConnecting"), SelectedDevice.Name);
        try
        {
            await session.ConnectAsync(SelectedDevice);
            IsConnected = true;
            ResetXt2443State();
            if (SelectedDevice.IsXt2443ControlTarget && session.Client is not null)
            {
                await LoadXt2443StateAsync(session.Client);
            }

            ShowStatus(LocalizationService.Get("StatusConnected"), SelectedDevice.Name);
        }
        catch (Exception exception)
        {
            await session.DisconnectAsync();
            IsConnected = false;
            ShowError(LocalizationService.Get("StatusConnectionFailed"), exception.Message);
            diagnostics.Write($"Connection failed: {exception}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> ConnectToDeviceAsync(MotoBudsDevice device)
    {
        if (IsBusy)
        {
            return false;
        }

        if (IsConnected && session.Device?.BluetoothAddress != device.BluetoothAddress)
        {
            await DisconnectAsync();
        }

        SelectedDevice = device;
        if (!IsConnected)
        {
            await ConnectAsync();
        }

        return IsConnected && session.Device?.BluetoothAddress == device.BluetoothAddress;
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        IsBusy = true;
        try
        {
            await session.DisconnectAsync();
            IsConnected = false;
            ResetXt2443State();
            ShowStatus(LocalizationService.Get("StatusDisconnected"), LocalizationService.Get("StatusDisconnectedMessage"));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private static async Task OpenBluetoothSettingsAsync() =>
        await Launcher.LaunchUriAsync(new Uri("ms-settings:bluetooth"));

    [RelayCommand]
    private async Task RefreshXt2443StateAsync()
    {
        if (session.Client is null || SelectedDevice?.IsXt2443ControlTarget != true)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await LoadXt2443StateAsync(session.Client);
            ShowStatus(LocalizationService.Get("StatusRefreshed"), LocalizationService.Get("StatusRefreshedMessage"));
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<CommandResult> SetAncAsync(int mode)
    {
        if (session.Client is null || !IsStableAncAvailable)
        {
            return new(false, false, false, LocalizationService.Get("StableAncUnavailable"));
        }

        IsBusy = true;
        try
        {
            var result = await session.Client.SetAncModeAsync((AncModeValue)mode);
            if (result.Success)
            {
                AncMode = mode;
                ShowStatus(LocalizationService.Get("StatusAncVerified"),
                    mode switch
                    {
                        0 => LocalizationService.Get("AncOff"),
                        1 => LocalizationService.Get("AncOn"),
                        _ => LocalizationService.Get("AncTransparency"),
                    });
            }
            else
            {
                ShowError(LocalizationService.Get("StatusAncFailed"), result.Error ?? LocalizationService.Get("UnknownError"));
            }

            return result;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<CommandResult> SetXt2443ToggleAsync(Xt2443ToggleFeature feature, bool enabled)
    {
        if (session.Client is null || !IsStableAncAvailable)
        {
            return new(false, false, false, LocalizationService.Get("Xt2443ControlsUnavailable"));
        }

        IsBusy = true;
        try
        {
            var result = await session.Client.SetXt2443ToggleAsync(feature, enabled);
            if (result.Success)
            {
                SetToggleState(feature, enabled);
                ShowStatus(LocalizationService.Get("StatusFeatureUpdated"),
                    LocalizationService.Get(enabled ? "FeatureOn" : "FeatureOff"));
            }
            else
            {
                ShowError(LocalizationService.Get("StatusFeatureFailed"),
                    result.Error ?? LocalizationService.Get("UnknownError"));
            }

            return result;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<CommandResult> SetDualConnectionAsync(bool enabled)
    {
        if (session.Client is null || !IsStableAncAvailable || !DualConnectionSupported)
        {
            return new(false, false, false, LocalizationService.Get("Xt2443ControlsUnavailable"));
        }

        IsBusy = true;
        try
        {
            var result = await session.Client.SetDualConnectionAsync(enabled);
            if (result.Success)
            {
                DualConnectionEnabled = enabled;
                ShowStatus(LocalizationService.Get("StatusFeatureUpdated"),
                    LocalizationService.Get(enabled ? "FeatureOn" : "FeatureOff"));
            }
            else
            {
                ShowError(LocalizationService.Get("StatusFeatureFailed"),
                    result.Error ?? LocalizationService.Get("UnknownError"));
            }

            return result;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<CommandResult> SetRingTargetAsync(EarbudRingTarget target)
    {
        if (session.Client is null || !IsStableAncAvailable)
        {
            return new(false, false, null, LocalizationService.Get("Xt2443ControlsUnavailable"));
        }

        IsBusy = true;
        try
        {
            var result = await session.Client.SetRingTargetAsync(target);
            if (result.Success)
            {
                IsFindActive = target != EarbudRingTarget.Stop;
                ShowStatus(LocalizationService.Get("FindEarbudsTitle"), target switch
                {
                    EarbudRingTarget.Left => LocalizationService.Get("FindLeftActive"),
                    EarbudRingTarget.Right => LocalizationService.Get("FindRightActive"),
                    _ => LocalizationService.Get("FindStopped"),
                });
            }
            else
            {
                ShowError(LocalizationService.Get("StatusFeatureFailed"),
                    result.Error ?? LocalizationService.Get("UnknownError"));
            }

            return result;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<CommandResult> SetFitTestAsync(bool enabled)
    {
        if (session.Client is null || !IsStableAncAvailable)
        {
            return new(false, false, null, LocalizationService.Get("Xt2443ControlsUnavailable"));
        }

        IsBusy = true;
        try
        {
            var result = await session.Client.SetFitTestAsync(enabled);
            if (result.Success)
            {
                IsFitTestActive = enabled;
                ShowStatus(LocalizationService.Get("FitTestTitle"),
                    LocalizationService.Get(enabled ? "FitTestStarted" : "FitTestStopped"));
            }
            else
            {
                ShowError(LocalizationService.Get("StatusFeatureFailed"),
                    result.Error ?? LocalizationService.Get("UnknownError"));
            }

            return result;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<CommandResult> RunExperimentalAsync(ExperimentalFeature feature, int value)
    {
        if (session.Client is null || SelectedDevice is null)
        {
            return new(false, false, null, LocalizationService.Get("ConnectFirst"));
        }

        IsBusy = true;
        try
        {
            var result = await session.Client.SetExperimentalFeatureAsync(feature, value);
            ExperimentalStatus = result.Success
                ? string.Format(LocalizationService.Get("ExperimentalAck"), feature, value)
                : string.Format(LocalizationService.Get("ExperimentalFailed"), result.Error);
            if (!result.Success)
            {
                ShowError(LocalizationService.Get("ExperimentalCommandFailed"), result.Error ?? LocalizationService.Get("UnknownError"));
            }
            return result;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanUse(ExperimentalFeature feature) =>
        IsConnected && SelectedDevice is not null && ModelCatalog.Supports(SelectedDevice.ModelId, feature);

    private async Task LoadXt2443StateAsync(MotoBudsClient client)
    {
        await TryLoadAsync("battery", () =>
        {
            if (SelectedDevice is { } device && session.TryGetCachedBatteryState(out var state))
            {
                ApplyBatteryState(device, state);
            }

            return Task.CompletedTask;
        });
        await TryLoadAsync("ANC", async () => AncMode = (await client.GetAncStateAsync()).Mode);
        await TryLoadAsync("adaptive hearing", async () =>
            AdaptiveHearingEnabled = await client.GetXt2443ToggleAsync(Xt2443ToggleFeature.AdaptiveHearing));
        await TryLoadAsync("Hi-Res", async () =>
            HiResEnabled = await client.GetXt2443ToggleAsync(Xt2443ToggleFeature.HiRes));
        await TryLoadAsync("gaming mode", async () =>
            GamingModeEnabled = await client.GetXt2443ToggleAsync(Xt2443ToggleFeature.GamingMode));
        await TryLoadAsync("volume boost", async () =>
            VolumeBoostEnabled = await client.GetXt2443ToggleAsync(Xt2443ToggleFeature.VolumeBoost));
        await TryLoadAsync("in-ear detection", async () =>
            InEarDetectionEnabled = await client.GetXt2443ToggleAsync(Xt2443ToggleFeature.InEarDetection));
        await TryLoadAsync("dual connection", async () =>
        {
            var state = await client.GetDualConnectionStateAsync();
            DualConnectionSupported = state.IsSupported;
            DualConnectionEnabled = state.IsEnabled;
        });
    }

    private async Task TryLoadAsync(string feature, Func<Task> load)
    {
        try
        {
            await load();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            diagnostics.Write($"XT2443 {feature} read failed: {exception.Message}");
        }
    }

    private async Task RefreshHomeBatteryAsync(DeviceCardViewModel card)
    {
        var temporaryConnection = false;
        try
        {
            if (session.IsConnected)
            {
                if (session.Device?.BluetoothAddress != card.Device.BluetoothAddress)
                {
                    return;
                }
            }
            else
            {
                await session.ConnectAsync(card.Device);
                temporaryConnection = true;
            }

            if (session.TryGetCachedBatteryState(out var state))
            {
                ApplyBatteryState(card.Device, state);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            diagnostics.Write($"Home battery refresh failed for {card.Device.ModelId}: {exception.Message}");
        }
        finally
        {
            if (temporaryConnection)
            {
                await session.DisconnectAsync();
            }
        }
    }

    private void ApplyBatteryState(MotoBudsDevice device, EarbudBatteryState state)
    {
        var card = DeviceCards.FirstOrDefault(item =>
            item.Device.BluetoothAddress == device.BluetoothAddress);
        card?.UpdateBattery(state);

        if (SelectedDevice?.BluetoothAddress == device.BluetoothAddress)
        {
            LeftBatteryText = FormatBattery(state.Left);
            RightBatteryText = FormatBattery(state.Right);
            CaseBatteryText = FormatBattery(state.Case);
            LeftBatteryCharging = state.Left.IsCharging;
            RightBatteryCharging = state.Right.IsCharging;
            CaseBatteryCharging = state.Case.IsCharging;
        }

        _ = lowBatteryNotifications.EvaluateAsync(device, state);
    }

    public async Task RefreshBatteryFromCacheAsync()
    {
        if (!session.IsConnected || session.Device is not { } device)
        {
            return;
        }

        try
        {
            var state = await session.GetBatteryStateAsync();
            if (state is not null)
            {
                ApplyBatteryState(device, state);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            diagnostics.Write($"Battery cache refresh failed for {device.ModelId}: {exception.Message}");
        }
    }

    private void OnBatteryStateChanged(object? sender, BatteryStateChangedEventArgs args) =>
        RunOnUiThread(() => ApplyBatteryState(args.Device, args.State));

    private void OnSessionDisconnected(object? sender, SessionDisconnectedEventArgs args)
    {
        _ = lowBatteryNotifications.ResetAsync(args.Device);
        RunOnUiThread(() =>
        {
            if (SelectedDevice?.BluetoothAddress == args.Device.BluetoothAddress)
            {
                IsConnected = false;
                ResetXt2443State();
            }
        });
    }

    private void OnInAppLowBatteryRequested(object? sender, InAppLowBatteryRequestedEventArgs args) =>
        RunOnUiThread(() => ShowStatus(
            LocalizationService.Get("LowBatteryToastTitle"),
            string.Format(
                LocalizationService.Get("LowBatteryToastBody"),
                ProductCatalog.Get(args.Device.ModelId, args.Device.ColorId).ProductName,
                args.LowestPercent)));

    private static void RunOnUiThread(Action action)
    {
        if (App.DispatcherQueue.HasThreadAccess)
        {
            action();
        }
        else
        {
            App.DispatcherQueue.TryEnqueue(() => action());
        }
    }

    private void SetToggleState(Xt2443ToggleFeature feature, bool enabled)
    {
        switch (feature)
        {
            case Xt2443ToggleFeature.AdaptiveHearing:
                AdaptiveHearingEnabled = enabled;
                break;
            case Xt2443ToggleFeature.HiRes:
                HiResEnabled = enabled;
                break;
            case Xt2443ToggleFeature.GamingMode:
                GamingModeEnabled = enabled;
                break;
            case Xt2443ToggleFeature.VolumeBoost:
                VolumeBoostEnabled = enabled;
                break;
            case Xt2443ToggleFeature.InEarDetection:
                InEarDetectionEnabled = enabled;
                break;
        }
    }

    private void ResetXt2443State()
    {
        AncMode = -1;
        AdaptiveHearingEnabled = false;
        HiResEnabled = false;
        GamingModeEnabled = false;
        VolumeBoostEnabled = false;
        InEarDetectionEnabled = false;
        DualConnectionEnabled = false;
        DualConnectionSupported = false;
        IsFindActive = false;
        IsFitTestActive = false;
        LeftBatteryText = "—";
        RightBatteryText = "—";
        CaseBatteryText = "—";
        LeftBatteryCharging = false;
        RightBatteryCharging = false;
        CaseBatteryCharging = false;
    }

    private static string FormatBattery(BatteryComponentState state)
    {
        if (state.Percent is null)
        {
            return "—";
        }

        return $"{state.Percent}%";
    }

    private static string FormatBatteryStatus(string percent, bool isCharging) =>
        isCharging && percent != "—"
            ? string.Format(LocalizationService.Get("BatteryChargingFormat"), percent)
            : percent;

    private void ShowStatus(string title, string message)
    {
        StatusTitle = title;
        StatusMessage = message;
        StatusIsError = false;
        StatusIsOpen = true;
    }

    private void ShowError(string title, string message)
    {
        StatusTitle = title;
        StatusMessage = message;
        StatusIsError = true;
        StatusIsOpen = true;
    }
}
