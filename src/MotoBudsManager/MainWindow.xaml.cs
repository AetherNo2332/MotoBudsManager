using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using Microsoft.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using System.Runtime.InteropServices;
using Windows.Graphics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace MotoBudsManager;

/// <summary>
/// The application window. This hosts a Frame that displays pages. Add your
/// UI and logic to MainPage.xaml / MainPage.xaml.cs instead of here so you
/// can use Page features such as navigation events and the Loaded lifecycle.
/// </summary>
public sealed partial class MainWindow : Window
{
    private bool _exitRequested;
    private bool _cleanupCompleted;

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hwnd);

    public IRelayCommand RestoreWindowCommand { get; }

    public IAsyncRelayCommand ExitApplicationCommand { get; }

    public MainWindow()
    {
        RestoreWindowCommand = new RelayCommand(RestoreWindow);
        ExitApplicationCommand = new AsyncRelayCommand(ExitApplicationAsync);
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var scale = GetDpiForWindow(hwnd) / 96.0;
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = (int)(760 * scale);
            presenter.PreferredMinimumHeight = (int)(600 * scale);
        }
        AppWindow.Resize(new SizeInt32((int)(1100 * scale), (int)(760 * scale)));
        AppWindow.Closing += OnAppWindowClosing;
        Closed += OnClosed;
        TrayIcon.ForceCreate(enablesEfficiencyMode: false);
        App.Diagnostics.Write($"Tray icon registration completed: created={TrayIcon.IsCreated}.");

        // Navigate the root frame to the main page on startup.
        RootFrame.Navigate(typeof(MainPage));
    }

    public void ApplyTheme(ElementTheme theme) => RootLayout.RequestedTheme = theme;

    public void RestoreFromActivation() => RestoreWindow();

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_exitRequested)
        {
            return;
        }

        args.Cancel = true;
        this.Hide(enableEfficiencyMode: false);
        App.Diagnostics.Write("Main window hidden; the connected earbud session remains active in the notification area.");
    }

    private void RestoreWindow()
    {
        this.Show(disableEfficiencyMode: false);
        Activate();
        SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        if (RootFrame.Content is MainPage page)
        {
            _ = page.OnWindowRestoredAsync();
        }
    }

    private async Task ExitApplicationAsync()
    {
        if (_exitRequested)
        {
            return;
        }

        _exitRequested = true;
        await CleanupAsync();
        Close();
    }

    private async void OnClosed(object sender, WindowEventArgs args) => await CleanupAsync();

    private async Task CleanupAsync()
    {
        if (_cleanupCompleted)
        {
            return;
        }

        _cleanupCompleted = true;
        TrayIcon.Dispose();
        await App.DeviceMonitor.DisposeAsync();
        await App.Session.DisposeAsync();
        await App.LowBatteryNotifications.ShutdownAsync();
    }
}
