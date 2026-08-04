using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using MotoBudsManager.Core;
using MotoBudsManager.Core.Models;
using MotoBudsManager.Services;
using MotoBudsManager.ViewModels;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace MotoBudsManager;

public sealed partial class MainPage : Page
{
    private bool _loaded;
    private bool _updatingAnc;
    private bool _labAcknowledged;
    private CancellationTokenSource? _findAutoStop;

    public MainPageViewModel ViewModel { get; } =
        new(App.Discovery, App.Session, App.Diagnostics, App.LowBatteryNotifications);

    public MainPage()
    {
        InitializeComponent();
        AppNavigation.SelectedItem = NavHome;
        ViewModel.DeviceCards.CollectionChanged += (_, _) => RebuildDeviceNavigation();
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ViewModel.AncMode))
            {
                UpdateAncSelection();
            }
        };
    }

    public static Visibility InvertBoolToVisibility(bool value) =>
        value ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility BoolToVisibility(bool value) =>
        value ? Visibility.Visible : Visibility.Collapsed;

    public static InfoBarSeverity SeverityFromError(bool isError) =>
        isError ? InfoBarSeverity.Error : InfoBarSeverity.Informational;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        ApplySavedTheme();
        await ViewModel.RefreshCommand.ExecuteAsync(null);
    }

    private async void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var selectedTag = args.SelectedItemContainer?.Tag;
        if (selectedTag is MotoBudsDevice device)
        {
            if (!device.IsXt2443ControlTarget)
            {
                AppNavigation.SelectedItem = NavHome;
                await OpenUntestedDeviceAsync(device);
                return;
            }

            ViewModel.SelectedDevice = device;
            selectedTag = "Devices";
        }

        if (selectedTag is not string tag)
        {
            return;
        }

        HomePage.Visibility = tag == "Home" ? Visibility.Visible : Visibility.Collapsed;
        DevicesPage.Visibility = tag == "Devices" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = tag == "Settings" ? Visibility.Visible : Visibility.Collapsed;
        LabPage.Visibility = Visibility.Collapsed;
        DiagnosticsPage.Visibility = Visibility.Collapsed;

        if (tag == "Home" && ViewModel.DeviceCards.Count > 0)
        {
            NavDevices.IsExpanded = true;
        }

    }

    private async void OnHomeDeviceCardClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MotoBudsDevice selectedDevice })
        {
            return;
        }

        if (!selectedDevice.IsXt2443ControlTarget)
        {
            await OpenUntestedDeviceAsync(selectedDevice);
            return;
        }

        if (!await ViewModel.ConnectToDeviceAsync(selectedDevice))
        {
            return;
        }

        ViewModel.SelectedDevice = selectedDevice;
        var deviceItem = NavDevices.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => item.Tag is MotoBudsDevice device &&
                                    device.BluetoothAddress == selectedDevice.BluetoothAddress);
        AppNavigation.SelectedItem = deviceItem ?? NavDevices;
    }

    private async Task OpenUntestedDeviceAsync(MotoBudsDevice device)
    {
        var product = ProductCatalog.Get(device.ModelId, device.ColorId);
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = LocalizationService.Get("UntestedDeviceTitle"),
            Content = string.Format(LocalizationService.Get("UntestedDeviceBody"), product.ProductName, device.ModelId),
            PrimaryButtonText = LocalizationService.Get("LabConsentAccept"),
            CloseButtonText = LocalizationService.Get("Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        AutomationProperties.SetAutomationId(dialog, "UntestedDeviceDialog");
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        _labAcknowledged = true;
        ViewModel.SelectedDevice = device;
        await ViewModel.ConnectToDeviceAsync(device);
        AppNavigation.SelectedItem = NavSettings;
        ShowSettingsSection(LabPage);
    }

    private async void OnOpenSettingsLabClick(object sender, RoutedEventArgs e)
    {
        if (!_labAcknowledged)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = LocalizationService.Get("LabConsentTitle"),
                Content = LocalizationService.Get("LabConsentBody"),
                PrimaryButtonText = LocalizationService.Get("LabConsentAccept"),
                CloseButtonText = LocalizationService.Get("Cancel"),
                DefaultButton = ContentDialogButton.Close,
            };
            _labAcknowledged = await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        if (_labAcknowledged)
        {
            ShowSettingsSection(LabPage);
        }
    }

    private void OnOpenSettingsDiagnosticsClick(object sender, RoutedEventArgs e) =>
        ShowSettingsSection(DiagnosticsPage);

    private void OnBackToSettingsClick(object sender, RoutedEventArgs e) =>
        ShowSettingsSection(SettingsPage);

    private void ShowSettingsSection(UIElement section)
    {
        HomePage.Visibility = Visibility.Collapsed;
        DevicesPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = section == SettingsPage ? Visibility.Visible : Visibility.Collapsed;
        LabPage.Visibility = section == LabPage ? Visibility.Visible : Visibility.Collapsed;
        DiagnosticsPage.Visibility = section == DiagnosticsPage ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RebuildDeviceNavigation()
    {
        NavDevices.MenuItems.Clear();
        for (var index = 0; index < ViewModel.DeviceCards.Count; index++)
        {
            var card = ViewModel.DeviceCards[index];
            var item = new NavigationViewItem
            {
                Content = card.ProductName,
                Icon = new FontIcon
                {
                    FontFamily = (Microsoft.UI.Xaml.Media.FontFamily)Application.Current.Resources["SymbolThemeFontFamily"],
                    Glyph = "\uE7F6",
                },
                Tag = card.Device,
            };
            AutomationProperties.SetAutomationId(item, $"NavDevice-{index}");
            AutomationProperties.SetName(item, card.ProductName);
            ToolTipService.SetToolTip(item, $"{card.ProductName} · {card.ModelAndColor}");
            NavDevices.MenuItems.Add(item);
        }

        NavDevices.IsExpanded = ViewModel.DeviceCards.Count > 0;
    }

    private async void OnAncSelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_updatingAnc || !ViewModel.IsStableAncAvailable || sender.SelectedItem is null)
        {
            return;
        }

        var mode = sender.SelectedItem == AncOnItem
            ? 1
            : sender.SelectedItem == AncTransparencyItem ? 2 : 0;
        var result = await ViewModel.SetAncAsync(mode);
        if (!result.Success)
        {
            UpdateAncSelection();
        }
    }

    private async void OnXt2443ToggleToggled(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy || sender is not ToggleSwitch { Tag: string tag } toggle ||
            !Enum.TryParse<Xt2443ToggleFeature>(tag, out var feature))
        {
            return;
        }

        await ViewModel.SetXt2443ToggleAsync(feature, toggle.IsOn);
    }

    private async void OnDualConnectionToggled(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy || sender is not ToggleSwitch toggle)
        {
            return;
        }

        await ViewModel.SetDualConnectionAsync(toggle.IsOn);
    }

    private async void OnXt2443FindClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } ||
            !Enum.TryParse<EarbudRingTarget>(tag, out var target))
        {
            return;
        }

        var result = await ViewModel.SetRingTargetAsync(target);
        if (result.Success)
        {
            ScheduleFindAutoStop(target == EarbudRingTarget.Stop ? 0 : 1, true);
        }
    }

    private async void OnFitTestClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } || !bool.TryParse(tag, out var enabled))
        {
            return;
        }

        await ViewModel.SetFitTestAsync(enabled);
    }

    private async void OnExperimentalButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string commandTag } ||
            !TryParseExperimentalTag(commandTag, out var feature, out var value))
        {
            return;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = LocalizationService.Get("ExperimentalConfirmTitle"),
            Content = string.Format(LocalizationService.Get("ExperimentalConfirmBody"), feature, value),
            PrimaryButtonText = LocalizationService.Get("SendCommand"),
            CloseButtonText = LocalizationService.Get("Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var result = await ViewModel.RunExperimentalAsync(feature, value);
        if (result.Success && feature == ExperimentalFeature.FindEarbuds)
        {
            ScheduleFindAutoStop(value, false);
        }
    }

    private async void OnExportLogClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileSavePicker
        {
            SuggestedFileName = $"motobuds-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}",
        };
        picker.FileTypeChoices.Add(LocalizationService.Get("TextFile"), [".txt"]);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, App.WindowHandle);
        var file = await picker.PickSaveFileAsync();
        if (file is not null)
        {
            await FileIO.WriteTextAsync(file, App.Diagnostics.ExportText());
        }
    }

    private void OnClearLogClick(object sender, RoutedEventArgs e) => App.Diagnostics.Entries.Clear();

    private void OnThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeSelector.SelectedItem is not ComboBoxItem { Tag: string tag })
        {
            return;
        }

        var theme = tag switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        if (App.Window is MainWindow window)
        {
            window.ApplyTheme(theme);
        }
        ApplicationData.Current.LocalSettings.Values["Theme"] = tag;
    }

    private void ApplySavedTheme()
    {
        var saved = ApplicationData.Current.LocalSettings.Values["Theme"] as string ?? "Default";
        ThemeSelector.SelectedItem = ThemeSelector.Items
            .OfType<ComboBoxItem>()
            .First(item => string.Equals(item.Tag as string, saved, StringComparison.Ordinal));
    }

    private void UpdateAncSelection()
    {
        _updatingAnc = true;
        AncSelector.SelectedItem = ViewModel.AncMode switch
        {
            0 => AncOffItem,
            1 => AncOnItem,
            2 => AncTransparencyItem,
            _ => null,
        };
        _updatingAnc = false;
    }

    private static bool TryParseExperimentalTag(string tag, out ExperimentalFeature feature, out int value)
    {
        feature = default;
        value = default;
        var parts = tag.Split(':', 2);
        return parts.Length == 2 &&
               Enum.TryParse(parts[0], out feature) &&
               int.TryParse(parts[1], out value);
    }

    private void ScheduleFindAutoStop(int value, bool hardwareVerified)
    {
        _findAutoStop?.Cancel();
        _findAutoStop?.Dispose();
        _findAutoStop = null;
        if (value == 0)
        {
            return;
        }

        _findAutoStop = new CancellationTokenSource();
        _ = AutoStopFindAsync(hardwareVerified, _findAutoStop.Token);
    }

    private async Task AutoStopFindAsync(bool hardwareVerified, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            if (hardwareVerified)
            {
                await ViewModel.SetRingTargetAsync(EarbudRingTarget.Stop);
            }
            else
            {
                await ViewModel.RunExperimentalAsync(ExperimentalFeature.FindEarbuds, 0);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
