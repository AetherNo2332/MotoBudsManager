using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using MotoBudsManager.Core;
using MotoBudsManager.Core.Models;

namespace MotoBudsManager.Services;

public sealed class EarbudMonitoringService : IAsyncDisposable
{
    private readonly DiagnosticLogService _log;
    private readonly bool _isTestMode;
    private readonly IEarbudConnectionSource _source;
    private readonly EarbudMonitoringCoordinator _coordinator;
    private readonly CancellationTokenSource _stop = new();
    private Task? _runTask;
    private DateTimeOffset _nextErrorLog;

    public EarbudMonitoringService(DeviceDiscoveryService discovery, MotoBudsSessionService session,
        DiagnosticLogService log, bool isTestMode)
    {
        _log = log;
        _isTestMode = isTestMode;
#if DEBUG
        _source = isTestMode ? new DebugEarbudConnectionSource(discovery, session) : new WindowsEarbudConnectionSource(discovery);
#else
        _source = new WindowsEarbudConnectionSource(discovery);
#endif
        _coordinator = new(session);
        _coordinator.WindowsConnected += (_, device) => ShowNotification(device, "connected",
            LocalizationService.Get("EarbudConnectedNotificationTitle"),
            string.Format(LocalizationService.Get("EarbudConnectedNotificationBody"), ProductName(device)));
        _coordinator.AncObserved += (_, observation) =>
        {
            AncObserved?.Invoke(this, observation);
            if (observation.ModeChanged)
            {
                var mode = LocalizationService.Get(observation.State.Mode switch
                {
                    0 => "AncOff",
                    1 => "AncOn",
                    _ => "AncTransparency",
                });
                ShowNotification(observation.Device, "anc", LocalizationService.Get("AncChangedNotificationTitle"),
                    string.Format(LocalizationService.Get("AncChangedNotificationBody"), ProductName(observation.Device), mode));
            }
        };
    }

    public event EventHandler<AncObservation>? AncObserved;

    public void Start() => _runTask ??= RunAsync(_stop.Token);

    public void SuppressUntilWindowsReconnect(ulong address) => _coordinator.SuppressUntilWindowsReconnect(address);

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (_runTask is not null)
        {
            try { await _runTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        _stop.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        _log.Write("Earbud status monitoring started: 2-second interval; automatic management connection enabled.");
        do
        {
            try
            {
                using var cycle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cycle.CancelAfter(TimeSpan.FromSeconds(15));
                var connected = await _source.GetConnectedAsync(cycle.Token).ConfigureAwait(false);
                await _coordinator.PollAsync(connected, DateTimeOffset.UtcNow, cycle.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                if (DateTimeOffset.UtcNow >= _nextErrorLog)
                {
                    _log.Write($"Earbud status monitoring deferred: {exception.Message}");
                    _nextErrorLog = DateTimeOffset.UtcNow.AddSeconds(30);
                }
            }
        } while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
    }

    private void ShowNotification(MotoBudsDevice device, string tag, string title, string body)
    {
        if (_isTestMode)
        {
            _log.Write($"DEBUG status notification suppressed [{tag}]: {body}");
            return;
        }
        try
        {
            var notification = new AppNotificationBuilder().AddText(title).AddText(body).BuildNotification();
            notification.Tag = tag;
            notification.Group = device.ModelId;
            AppNotificationManager.Default.Show(notification);
        }
        catch (Exception exception) { _log.Write($"Earbud status notification failed: {exception.Message}"); }
    }

    private static string ProductName(MotoBudsDevice device) => ProductCatalog.Get(device.ModelId, device.ColorId).ProductName;
}
