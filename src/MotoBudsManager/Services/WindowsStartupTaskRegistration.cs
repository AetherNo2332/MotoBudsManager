using MotoBudsManager.Core;
using Windows.ApplicationModel;

namespace MotoBudsManager.Services;

public sealed class WindowsStartupTaskRegistration : IStartupTaskRegistration
{
    public const string TaskId = "MotoBudsManagerStartup";
    private StartupTask? _task;

    public async Task<StartupRegistrationState> GetStateAsync()
    {
        _task ??= await StartupTask.GetAsync(TaskId);
        return Map(_task.State);
    }

    public async Task<StartupRegistrationState> RequestEnableAsync()
    {
        _task ??= await StartupTask.GetAsync(TaskId);
        return Map(await _task.RequestEnableAsync());
    }

    public void Disable() => (_task ?? throw new InvalidOperationException("Startup task has not been loaded.")).Disable();

    private static StartupRegistrationState Map(StartupTaskState state) => state switch
    {
        StartupTaskState.Disabled => StartupRegistrationState.Disabled,
        StartupTaskState.Enabled => StartupRegistrationState.Enabled,
        StartupTaskState.DisabledByUser => StartupRegistrationState.DisabledByUser,
        StartupTaskState.DisabledByPolicy => StartupRegistrationState.DisabledByPolicy,
        StartupTaskState.EnabledByPolicy => StartupRegistrationState.EnabledByPolicy,
        _ => StartupRegistrationState.Unavailable,
    };
}
