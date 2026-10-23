using MotoBudsManager.Core;

namespace MotoBudsManager.Tests;

public class StartupRegistrationTests
{
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void LaunchPolicy_OnlyStartupOrExplicitBackgroundLaunchesStayHidden(bool startup, bool background, bool hidden)
    {
        Assert.Equal(hidden, StartupLaunchPolicy.ShouldStartHidden(startup, background ? ["--background"] : []));
    }

    [Theory]
    [InlineData("--BACKGROUND", true)]
    [InlineData("--background=false", false)]
    [InlineData("file--background.txt", false)]
    public void LaunchPolicy_RequiresAnExactBackgroundArgument(string argument, bool hidden)
    {
        Assert.Equal(hidden, StartupLaunchPolicy.ShouldStartHidden(false, [argument]));
    }

    [Fact]
    public async Task Enable_RegistersStartupAndReportsActualSystemState()
    {
        var registration = new Registration(StartupRegistrationState.Disabled);
        var controller = new StartupRegistrationController(registration);
        Assert.Equal(StartupRegistrationState.Enabled, await controller.SetEnabledAsync(true));
        Assert.Equal(StartupRegistrationState.Enabled, await controller.GetStateAsync());
    }

    [Fact]
    public async Task Disable_UnregistersEnabledStartup()
    {
        var registration = new Registration(StartupRegistrationState.Enabled);
        var controller = new StartupRegistrationController(registration);
        Assert.Equal(StartupRegistrationState.Disabled, await controller.SetEnabledAsync(false));
        Assert.Equal(StartupRegistrationState.Disabled, await controller.GetStateAsync());
    }

    [Theory]
    [InlineData(StartupRegistrationState.DisabledByUser, true)]
    [InlineData(StartupRegistrationState.DisabledByPolicy, true)]
    [InlineData(StartupRegistrationState.EnabledByPolicy, false)]
    public async Task SetEnabled_RespectsUserAndPolicyRestrictions(StartupRegistrationState state, bool enabled)
    {
        var registration = new Registration(state, rejectChanges: true);
        Assert.Equal(state, await new StartupRegistrationController(registration).SetEnabledAsync(enabled));
    }

    [Fact]
    public async Task Enable_DeniedRequestDoesNotPretendStartupIsEnabled()
    {
        var registration = new Registration(StartupRegistrationState.Disabled, enableResult: StartupRegistrationState.DisabledByUser);
        Assert.Equal(StartupRegistrationState.DisabledByUser,
            await new StartupRegistrationController(registration).SetEnabledAsync(true));
    }

    [Fact]
    public async Task Enable_SystemFailureIsNotReportedAsSuccess()
    {
        var registration = new Registration(StartupRegistrationState.Disabled, rejectChanges: true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new StartupRegistrationController(registration).SetEnabledAsync(true));
    }

    private sealed class Registration(
        StartupRegistrationState initial,
        bool rejectChanges = false,
        StartupRegistrationState enableResult = StartupRegistrationState.Enabled) : IStartupTaskRegistration
    {
        private StartupRegistrationState _state = initial;
        public Task<StartupRegistrationState> GetStateAsync() => Task.FromResult(_state);
        public Task<StartupRegistrationState> RequestEnableAsync()
        {
            if (rejectChanges) { throw new InvalidOperationException("Registration cannot be changed."); }
            _state = enableResult;
            return Task.FromResult(_state);
        }
        public void Disable()
        {
            if (rejectChanges) { throw new InvalidOperationException("Registration cannot be changed."); }
            _state = StartupRegistrationState.Disabled;
        }
    }
}
