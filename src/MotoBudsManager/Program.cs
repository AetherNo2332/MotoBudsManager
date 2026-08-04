using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using System.Runtime.InteropServices;

namespace MotoBudsManager;

public static class Program
{
    private const string MainInstanceKey = "MotoBudsManager.MainInstance";

    internal static AppInstance MainInstance { get; private set; } = null!;

    [STAThread]
    public static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        var currentInstance = AppInstance.GetCurrent();
        var activatedArgs = currentInstance.GetActivatedEventArgs();
        var mainInstance = AppInstance.FindOrRegisterForKey(MainInstanceKey);
        if (!mainInstance.IsCurrent)
        {
            RedirectActivationTo(activatedArgs, mainInstance);
            return 0;
        }

        MainInstance = mainInstance;

        Application.Start(initializationCallbackParams =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });

        return 0;
    }

    private static void RedirectActivationTo(AppActivationArguments args, AppInstance mainInstance)
    {
        var eventHandle = CreateEvent(nint.Zero, true, false, null);
        if (eventHandle == nint.Zero)
        {
            throw new InvalidOperationException("Could not create the activation redirection event.");
        }

        try
        {
            _ = Task.Run(() =>
            {
                try
                {
                    mainInstance.RedirectActivationToAsync(args).AsTask().GetAwaiter().GetResult();
                }
                finally
                {
                    SetEvent(eventHandle);
                }
            });

            _ = CoWaitForMultipleObjects(
                0,
                uint.MaxValue,
                1,
                [eventHandle],
                out _);
        }
        finally
        {
            CloseHandle(eventHandle);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateEvent(
        nint eventAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool manualReset,
        [MarshalAs(UnmanagedType.Bool)] bool initialState,
        string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetEvent(nint eventHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("ole32.dll")]
    private static extern uint CoWaitForMultipleObjects(
        uint flags,
        uint timeoutMilliseconds,
        ulong handleCount,
        nint[] handles,
        out uint index);
}
