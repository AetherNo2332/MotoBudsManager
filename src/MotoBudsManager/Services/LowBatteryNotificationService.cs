using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using MotoBudsManager.Core;
using MotoBudsManager.Core.Models;

namespace MotoBudsManager.Services;

public sealed class LowBatteryNotificationService(
    DiagnosticLogService diagnostics,
    bool suppressSystemNotifications = false)
{
    private readonly LowBatteryAlertTracker _tracker = new();
    private bool _registered;

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

    public void Evaluate(MotoBudsDevice device, EarbudBatteryState state)
    {
        var alert = _tracker.Evaluate(device.BluetoothAddress.ToString("X12"), state);
        if (alert is null)
        {
            return;
        }

        var components = FormatComponents(alert.Components);
        var body = string.Format(
            LocalizationService.Get("LowBatteryNotificationBody"),
            ProductCatalog.Get(device.ModelId, device.ColorId).ProductName,
            components,
            alert.LowestPercent);

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
            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception exception)
        {
            diagnostics.Write($"Low-battery notification failed: {exception.Message}");
        }
    }

    public void Shutdown()
    {
        if (!_registered)
        {
            return;
        }

        try
        {
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

    private static string FormatComponents(BatteryComponentKind components)
    {
        var names = new List<string>(3);
        if ((components & BatteryComponentKind.Left) != 0)
        {
            names.Add(LocalizationService.Get("BatteryComponentLeft"));
        }
        if ((components & BatteryComponentKind.Right) != 0)
        {
            names.Add(LocalizationService.Get("BatteryComponentRight"));
        }
        if ((components & BatteryComponentKind.Case) != 0)
        {
            names.Add(LocalizationService.Get("BatteryComponentCase"));
        }
        return string.Join(LocalizationService.Get("BatteryComponentSeparator"), names);
    }
}
