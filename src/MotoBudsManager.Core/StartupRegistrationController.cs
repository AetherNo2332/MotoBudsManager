namespace MotoBudsManager.Core;

public enum StartupRegistrationState
{
    Unavailable,
    Disabled,
    Enabled,
    DisabledByUser,
    DisabledByPolicy,
    EnabledByPolicy,
}

public interface IStartupTaskRegistration
{
    Task<StartupRegistrationState> GetStateAsync();
    Task<StartupRegistrationState> RequestEnableAsync();
    void Disable();
}

public sealed class StartupRegistrationController(IStartupTaskRegistration registration)
{
    public Task<StartupRegistrationState> GetStateAsync() => registration.GetStateAsync();
    public async Task<StartupRegistrationState> SetEnabledAsync(bool enabled)
    {
        var state = await registration.GetStateAsync();
        if (enabled && state == StartupRegistrationState.Disabled)
        {
            return await registration.RequestEnableAsync();
        }
        if (!enabled && state == StartupRegistrationState.Enabled)
        {
            registration.Disable();
            return await registration.GetStateAsync();
        }
        return state;
    }
}

public static class StartupLaunchPolicy
{
    public static bool ShouldStartHidden(bool isStartupTask, IEnumerable<string> arguments) =>
        isStartupTask || arguments.Any(argument => argument.Equals("--background", StringComparison.OrdinalIgnoreCase));
}
