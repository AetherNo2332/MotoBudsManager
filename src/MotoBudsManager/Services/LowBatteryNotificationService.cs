using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using MotoBudsManager.Core;
using MotoBudsManager.Core.Models;

namespace MotoBudsManager.Services;

public sealed class InAppLowBatteryRequestedEventArgs(MotoBudsDevice device, int lowestPercent) : EventArgs
{
    public MotoBudsDevice Device { get; } = device;

    public int LowestPercent { get; } = lowestPercent;
}

public sealed class LowBatteryNotificationService(
    DiagnosticLogService diagnostics,
    bool suppressSystemNotifications = false)
{
    private const string NotificationTag = "low-battery";
    private readonly LowBatteryAlertTracker _tracker = new();
    private readonly Lock _trackerLock = new();
    private bool _registered;

    public event EventHandler<InAppLowBatteryRequestedEventArgs>? InAppLowBatteryRequested;

    public void Initialize()
    {
        if (suppressSystemNotifications)
        {
            return;
        }

        try
        {
            AppNotificationManager.Default.Register();
            _registered = true;
        }
        catch (Exception exception)
        {
            diagnostics.Write($"App notification registration failed: {exception.Message}");
        }
    }

    public async Task EvaluateAsync(MotoBudsDevice device, EarbudBatteryState state)
    {
        LowBatteryEvaluation evaluation;
        lock (_trackerLock)
        {
            evaluation = _tracker.Evaluate(DeviceKey(device), state);
        }

        if (evaluation.ShowInAppToast && evaluation.LowestPercent is { } toastPercent)
        {
            InAppLowBatteryRequested?.Invoke(this, new(device, toastPercent));
        }

        if (evaluation.SystemAction == LowBatterySystemAction.None ||
            evaluation.LowestPercent is not { } lowestPercent)
        {
            return;
        }

        if (evaluation.SystemAction == LowBatterySystemAction.Cancel)
        {
            await RemoveSystemNotificationAsync(device).ConfigureAwait(false);
            return;
        }

        var body = string.Format(
            LocalizationService.Get("LowBatteryNotificationBody"),
            ProductCatalog.Get(device.ModelId, device.ColorId).ProductName,
            lowestPercent);

        if (suppressSystemNotifications)
        {
            diagnostics.Write($"DEBUG notification suppressed: {body}");
            return;
        }

        try
        {
            var notification = new AppNotificationBuilder()
                .AddText(LocalizationService.Get("LowBatteryNotificationTitle"))
                .AddText(body)
                .BuildNotification();
            notification.Tag = NotificationTag;
            notification.Group = DeviceKey(device);
            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception exception)
        {
            diagnostics.Write($"Low-battery notification failed: {exception.Message}");
        }
    }

    public async Task ResetAsync(MotoBudsDevice device)
    {
        lock (_trackerLock)
        {
            _tracker.Reset(DeviceKey(device));
        }

        await RemoveSystemNotificationAsync(device).ConfigureAwait(false);
    }

    public async Task ShutdownAsync()
    {
        if (!_registered)
        {
            return;
        }

        try
        {
            await AppNotificationManager.Default.RemoveAllAsync();
            AppNotificationManager.Default.Unregister();
        }
        catch (Exception exception)
        {
            diagnostics.Write($"App notification cleanup failed: {exception.Message}");
        }
        finally
        {
            _registered = false;
        }
    }

    private async Task RemoveSystemNotificationAsync(MotoBudsDevice device)
    {
        if (suppressSystemNotifications || !_registered)
        {
            return;
        }

        try
        {
            await AppNotificationManager.Default.RemoveByTagAndGroupAsync(NotificationTag, DeviceKey(device));
        }
        catch (Exception exception)
        {
            diagnostics.Write($"Low-battery notification removal failed: {exception.Message}");
        }
    }

    private static string DeviceKey(MotoBudsDevice device) => device.BluetoothAddress.ToString("X12");
}
