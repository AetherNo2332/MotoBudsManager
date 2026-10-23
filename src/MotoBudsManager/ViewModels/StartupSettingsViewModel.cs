using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MotoBudsManager.Core;
using MotoBudsManager.Services;
using Windows.System;

namespace MotoBudsManager.ViewModels;

public partial class StartupSettingsViewModel(
    StartupRegistrationController controller,
    DiagnosticLogService diagnostics) : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOn))]
    [NotifyPropertyChangedFor(nameof(CanChange))]
    public partial StartupRegistrationState State { get; set; } = StartupRegistrationState.Unavailable;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChange))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotice))]
    public partial string Notice { get; set; } = string.Empty;

    public bool IsOn => State is StartupRegistrationState.Enabled or StartupRegistrationState.EnabledByPolicy;
    public string ToggleLabel => LocalizationService.Get("StartupToggleLabel");
    public bool CanChange => !IsBusy && (State is StartupRegistrationState.Disabled or StartupRegistrationState.Enabled);
    public bool HasNotice => !string.IsNullOrEmpty(Notice);

    public Task RefreshAsync() => UpdateAsync(null);

    [RelayCommand]
    private Task SetEnabledAsync(bool enabled) => UpdateAsync(enabled);

    [RelayCommand]
    private async Task OpenStartupSettingsAsync()
    {
        try
        {
            if (!await Launcher.LaunchUriAsync(new Uri("ms-settings:startupapps")))
            {
                Notice = LocalizationService.Get("StartupUnavailable");
            }
        }
        catch (Exception exception)
        {
            diagnostics.Write($"Could not open Windows Startup apps: {exception.Message}");
            Notice = LocalizationService.Get("StartupUnavailable");
        }
    }

    private async Task UpdateAsync(bool? enabled)
    {
        if (IsBusy) { return; }
        IsBusy = true;
        try
        {
            State = enabled.HasValue
                ? await controller.SetEnabledAsync(enabled.Value)
                : await controller.GetStateAsync();
            Notice = State switch
            {
                StartupRegistrationState.DisabledByUser => LocalizationService.Get("StartupDisabledByUser"),
                StartupRegistrationState.DisabledByPolicy or StartupRegistrationState.EnabledByPolicy => LocalizationService.Get("StartupManagedByPolicy"),
                StartupRegistrationState.Unavailable => LocalizationService.Get("StartupUnavailable"),
                _ => string.Empty,
            };
        }
        catch (Exception exception)
        {
            diagnostics.Write($"Startup task update failed: {exception.Message}");
            // A failed request must not leave the switch claiming an unconfirmed state.
            try { State = await controller.GetStateAsync(); }
            catch { State = StartupRegistrationState.Unavailable; }
            Notice = LocalizationService.Get("StartupUnavailable");
        }
        finally
        {
            OnPropertyChanged(nameof(IsOn));
            IsBusy = false;
        }
    }
}
