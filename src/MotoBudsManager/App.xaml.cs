using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Windows.AppLifecycle;
using MotoBudsManager.Core;
using MotoBudsManager.Services;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace MotoBudsManager;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private readonly AppInstance _mainInstance;
    private int _activationPending;

    public static bool IsTestMode
    {
        get
        {
#if DEBUG
            return Environment.GetEnvironmentVariable("MOTOBUDS_TEST_MODE") == "1" ||
                   Environment.GetCommandLineArgs().Any(argument =>
                       argument.Equals("--test-mode", StringComparison.OrdinalIgnoreCase));
#else
            return false;
#endif
        }
    }

    public static DiagnosticLogService Diagnostics { get; } = new();

    public static DeviceDiscoveryService Discovery { get; } = new(Diagnostics, IsTestMode);

    public static MotoBudsSessionService Session { get; } = new(Diagnostics, IsTestMode);

    public static LowBatteryNotificationService LowBatteryNotifications { get; } =
        new(Diagnostics, IsTestMode);

    public static string AppVersionText { get; } = PackageVersionReader.GetDisplayText(
        "Moto Buds Manager",
        Path.Combine(AppContext.BaseDirectory, "AppxManifest.xml"));

    /// <summary>
    /// The main application window. Use <c>App.Window</c> from any class that needs
    /// the window reference (for dialogs, pickers, interop, etc.).
    /// </summary>
    public static Window Window { get; private set; } = null!;

    /// <summary>
    /// The UI thread dispatcher. Use <c>App.DispatcherQueue</c> to marshal calls
    /// to the UI thread. Fully qualified to avoid CS0104 ambiguity with
    /// <see cref="Windows.System.DispatcherQueue"/>.
    /// </summary>
    public static Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue { get; private set; } = null!;

    /// <summary>
    /// The native window handle (HWND). Use for file pickers,
    /// <c>DataTransferManager</c>, and any WinRT interop that requires
    /// <c>InitializeWithWindow</c>.
    /// </summary>
    public static nint WindowHandle =>
        WinRT.Interop.WindowNative.GetWindowHandle(Window);

    /// <summary>
    /// Initializes the singleton application object.
    /// </summary>
    public App()
    {
        _mainInstance = Program.MainInstance;
        _mainInstance.Activated += OnMainInstanceActivated;
        InitializeComponent();
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        LowBatteryNotifications.Initialize();
        Window = new MainWindow();
        Diagnostics.Write(IsTestMode
            ? "Moto Buds Manager started in DEBUG fake-device mode."
            : "Moto Buds Manager started. No telemetry or network service is enabled.");
        Window.Activate();
        if (Interlocked.Exchange(ref _activationPending, 0) != 0)
        {
            RestoreExistingWindow();
        }
    }

    private void OnMainInstanceActivated(object? sender, AppActivationArguments args)
    {
        if (DispatcherQueue is null || Window is not MainWindow)
        {
            Interlocked.Exchange(ref _activationPending, 1);
            return;
        }

        DispatcherQueue.TryEnqueue(RestoreExistingWindow);
    }

    private static void RestoreExistingWindow()
    {
        Diagnostics.Write("A secondary launch was redirected to the existing app instance.");
        if (Window is MainWindow window)
        {
            window.RestoreFromActivation();
        }
    }
}
