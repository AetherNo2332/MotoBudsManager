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
    private const string BoseLogoDisplayModeSettingKey = "BoseLogoDisplayMode";
    private bool _loaded;
    private bool _updatingAnc;
    private bool _labAcknowledged;
    private bool _fitTestDialogOpen;
    private CancellationTokenSource? _findAutoStop;

    public MainPageViewModel ViewModel { get; } =
        new(App.Discovery, App.Session, App.Diagnostics, App.LowBatteryNotifications);

    public MainPage()
    {
        InitializeComponent();
        AboutVersionText.Text = App.AppVersionText;
        AppNavigation.SelectedItem = NavHome;
        ViewModel.DeviceCards.CollectionChanged += (_, _) => RebuildDeviceNavigation();
        ViewModel.ConnectionLost += (_, _) =>
        {
            if (_fitTestDialogOpen)
            {
                FitTestDialog.Hide();
            }

            AppNavigation.SelectedItem = NavHome;
        };
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ViewModel.AncMode))
            {
                UpdateAncSelection();
            }
            else if (args.PropertyName == nameof(ViewModel.SpatialAudioMode))
            {
                UpdateSpatialSelection();
            }
        };
    }

    public static Visibility InvertBoolToVisibility(bool value) =>
        value ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility BoolToVisibility(bool value) =>
        value ? Visibility.Visible : Visibility.Collapsed;

    public static InfoBarSeverity SeverityFromError(bool isError) =>
        isError ? InfoBarSeverity.Error : InfoBarSeverity.Informational;

    public static string FitTestStatusGlyph(FitTestEarStatus status) => status switch
    {
        FitTestEarStatus.Passed => "\uE73E",
        FitTestEarStatus.Failed => "\uE711",
        _ => "\uE9CE",
    };

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        ApplySavedTheme();
        ApplySavedBoseLogoDisplayMode();
        await ViewModel.Startup.RefreshAsync();
        await ViewModel.RefreshCommand.ExecuteAsync(null);
    }

    private async void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var selectedTag = args.SelectedItemContainer?.Tag;
        if (selectedTag is MotoBudsDevice device)
        {
            if (!await ViewModel.ConnectToDeviceAsync(device))
            {
                AppNavigation.SelectedItem = NavHome;
                return;
            }

            selectedTag = "Devices";
            ResetDeviceControlScroll();
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

        if (tag == "Settings")
        {
            await ViewModel.Startup.RefreshAsync();
        }
        else if (tag == "Devices" && args.SelectedItemContainer == NavDevices && ViewModel.IsConnected && !ViewModel.IsBusy)
        {
            await ViewModel.RefreshDeviceStateCommand.ExecuteAsync(null);
        }

        if (tag == "Home" && ViewModel.DeviceCards.Count > 0)
        {
            NavDevices.IsExpanded = true;
        }
        else if (tag == "Devices")
        {
            await ViewModel.RefreshBatteryFromCacheAsync();
        }
    }

    public async Task OnWindowRestoredAsync()
    {
        await ViewModel.Startup.RefreshAsync();
        await ViewModel.RefreshBatteryFromCacheAsync();
    }

    private async void OnStartupToggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle && ViewModel.Startup.CanChange && toggle.IsOn != ViewModel.Startup.IsOn)
        {
            await ViewModel.Startup.SetEnabledCommand.ExecuteAsync(toggle.IsOn);
        }
    }

    private async void OnDisconnectClick(object sender, RoutedEventArgs e)
    {
        await ViewModel.DisconnectCommand.ExecuteAsync(null);
        AppNavigation.SelectedItem = NavHome;
    }

    private async void OnHomeDeviceCardClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MotoBudsDevice selectedDevice })
        {
            return;
        }

        if (!await ViewModel.ConnectToDeviceAsync(selectedDevice))
        {
            return;
        }

        ViewModel.SelectedDevice = selectedDevice;
        ResetDeviceControlScroll();
        var deviceItem = NavDevices.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => item.Tag is MotoBudsDevice device &&
                                    device.BluetoothAddress == selectedDevice.BluetoothAddress);
        AppNavigation.SelectedItem = deviceItem ?? NavDevices;
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
        if (_updatingAnc || !ViewModel.CanUseAnc || sender.SelectedItem is null)
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

    private async void OnFeatureToggleToggled(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy || sender is not ToggleSwitch { Tag: string tag } toggle ||
            !Enum.TryParse<ExperimentalFeature>(tag, out var feature))
        {
            return;
        }

        await ViewModel.SetFeatureAsync(feature, toggle.IsOn ? 1 : 0);
    }

    private async void OnSpatialSelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (_updatingAnc || !ViewModel.CanUseSpatialAudio || sender.SelectedItem is null)
        {
            return;
        }

        var mode = sender.SelectedItem == SpatialOnItem
            ? 1
            : sender.SelectedItem == SpatialTrackingItem ? 2 : 0;
        await ViewModel.SetFeatureAsync(ExperimentalFeature.SpatialAudio, mode);
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

    private async void OnOpenFitTestDialogClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanUseFitTest)
        {
            return;
        }

        ViewModel.PrepareFitTest();
        FitTestDialog.XamlRoot = XamlRoot;
        _fitTestDialogOpen = true;
        try
        {
            await FitTestDialog.ShowAsync();
        }
        finally
        {
            _fitTestDialogOpen = false;
        }
    }

    private async void OnFitTestStartClick(object sender, RoutedEventArgs e) =>
        await ViewModel.SetFitTestAsync(true);

    private async void OnFitTestStopClick(object sender, RoutedEventArgs e) =>
        await ViewModel.SetFitTestAsync(false);

    private async void OnFitTestDialogClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (!ViewModel.IsFitTestActive)
        {
            return;
        }

        var deferral = args.GetDeferral();
        try
        {
            var result = await ViewModel.SetFitTestAsync(false);
            args.Cancel = !result.Success;
        }
        finally
        {
            deferral.Complete();
        }
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
            Content = string.Format(LocalizationService.Get("ExperimentalConfirmBody"),
                LocalizationService.Get($"FeatureName{feature}")),
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

    private void OnBoseLogoModeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BoseLogoModeSelector.SelectedItem is not ComboBoxItem { Tag: string tag } ||
            !Enum.TryParse<BoseLogoDisplayMode>(tag, ignoreCase: true, out var mode))
        {
            return;
        }

        ViewModel.BoseLogoDisplayMode = mode;
        ApplicationData.Current.LocalSettings.Values[BoseLogoDisplayModeSettingKey] = mode.ToString();
    }

    private void ApplySavedBoseLogoDisplayMode()
    {
        var saved = ApplicationData.Current.LocalSettings.Values[BoseLogoDisplayModeSettingKey] as string;
        var mode = Enum.TryParse<BoseLogoDisplayMode>(saved, ignoreCase: true, out var parsed)
            ? parsed
            : BoseLogoDisplayMode.ByModel;
        BoseLogoModeSelector.SelectedItem = BoseLogoModeSelector.Items
            .OfType<ComboBoxItem>()
            .First(item => string.Equals(item.Tag as string, mode.ToString(), StringComparison.Ordinal));
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

    private void ResetDeviceControlScroll() =>
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            DevicePageScrollViewer.ChangeView(0, 0, null, true);
            DeviceControlScrollViewer.ChangeView(0, 0, null, true);
        });

    private void OnDeviceDetailSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // The full-width page keeps the product and battery visible while controls scroll.
        var splitView = e.NewSize.Width >= 880;
        ViewModel.IsDetailSplitView = splitView;
        DeviceBodyGrid.Height = splitView ? e.NewSize.Height : double.NaN;
        DeviceBodyGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        DeviceBodyGrid.ColumnDefinitions[1].Width = splitView
            ? new GridLength(1.15, GridUnitType.Star)
            : new GridLength(0);
        DeviceBodyGrid.ColumnSpacing = splitView ? 24 : 0;
        DeviceBodyGrid.RowDefinitions[0].Height = splitView
            ? new GridLength(1, GridUnitType.Star)
            : GridLength.Auto;
        DeviceBodyGrid.RowDefinitions[1].Height = splitView
            ? new GridLength(0)
            : GridLength.Auto;
        DeviceVisualPane.RowDefinitions[0].Height = splitView
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(280);
        Grid.SetColumn(DeviceControlScrollViewer, splitView ? 1 : 0);
        Grid.SetRow(DeviceControlScrollViewer, splitView ? 0 : 1);
        DevicePageScrollViewer.VerticalScrollBarVisibility = splitView
            ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
        DevicePageScrollViewer.VerticalScrollMode = splitView
            ? ScrollMode.Disabled : ScrollMode.Auto;
        DeviceControlScrollViewer.VerticalScrollBarVisibility = splitView
            ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        DeviceControlScrollViewer.VerticalScrollMode = splitView
            ? ScrollMode.Auto : ScrollMode.Disabled;
        AutomationProperties.SetAutomationId(DevicePageScrollViewer,
            splitView ? "DeviceOverviewScroll" : "DeviceControlScroll");
        AutomationProperties.SetAutomationId(DeviceControlScrollViewer,
            splitView ? "DeviceControlScroll" : "DeviceFeatureScroll");

        var featureWidth = splitView ? (e.NewSize.Width - 24) * 1.15 / 2.15 : e.NewSize.Width;
        var twoColumns = featureWidth >= 640;
        ArrangeFeatureGroup(ListeningFeatureGrid, twoColumns);
        ArrangeFeatureGroup(SoundFeatureGrid, twoColumns);
        ArrangeFeatureGroup(WearingFeatureGrid, twoColumns);
        ArrangeFeatureGroup(ToolsFeatureGrid, twoColumns);

    }

    private static void ArrangeFeatureGroup(Grid group, bool twoColumns)
    {
        group.ColumnDefinitions[1].Width = twoColumns
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(0);
        group.ColumnSpacing = twoColumns ? 12 : 0;
        for (var index = 0; index < group.Children.Count; index++)
        {
            var child = (FrameworkElement)group.Children[index];
            Grid.SetColumn(child, twoColumns ? index % 2 : 0);
            Grid.SetRow(child, twoColumns ? index / 2 : index);
        }
    }

    private void UpdateSpatialSelection()
    {
        _updatingAnc = true;
        SpatialSelector.SelectedItem = ViewModel.SpatialAudioMode switch
        {
            0 => SpatialOffItem,
            1 => SpatialOnItem,
            2 => SpatialTrackingItem,
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
