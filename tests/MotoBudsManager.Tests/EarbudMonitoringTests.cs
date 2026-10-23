using MotoBudsManager.Core;
using MotoBudsManager.Core.Models;

namespace MotoBudsManager.Tests;

public class EarbudMonitoringTests
{
    private static readonly MotoBudsDevice Device = new("test", "moto buds", 1, "XT2443-1", 3, 0x10, TransportKind.Rfcomm, EvidenceLevel.HardwareVerified);
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-24T00:00:00Z");

    [Fact]
    public async Task Poll_ConnectsOnceAndNotifiesWindowsConnectionOnce()
    {
        var session = new Session();
        var monitor = new EarbudMonitoringCoordinator(session);
        var connections = new List<MotoBudsDevice>();
        monitor.WindowsConnected += (_, device) => connections.Add(device);
        await monitor.PollAsync([Device, Device], Now);
        await monitor.PollAsync([Device], Now.AddSeconds(2));
        Assert.Single(connections);
        Assert.Equal(1, session.ConnectCalls);
        Assert.Equal(2, session.ReadCalls);
    }

    [Fact]
    public async Task Poll_FirstReadIsBaseline_OnlyLaterModeChangesNotify()
    {
        var session = new Session();
        var monitor = new EarbudMonitoringCoordinator(session);
        var observations = new List<AncObservation>();
        monitor.AncObserved += (_, observation) => observations.Add(observation);
        await monitor.PollAsync([Device], Now);
        session.Anc = new(0, 0);
        await monitor.PollAsync([Device], Now.AddSeconds(2));
        await monitor.PollAsync([Device], Now.AddSeconds(4));
        Assert.False(observations[0].ModeChanged);
        Assert.Single(observations, item => item.ModeChanged);
        Assert.Equal((byte)0, observations.Single(item => item.ModeChanged).State.Mode);
    }

    [Fact]
    public async Task Poll_SubtypeOnlyChangeDoesNotRepeatTheModeNotification()
    {
        var session = new Session();
        var monitor = new EarbudMonitoringCoordinator(session);
        var changes = new List<AncObservation>();
        monitor.AncObserved += (_, observation) => { if (observation.ModeChanged) changes.Add(observation); };
        await monitor.PollAsync([Device], Now);
        session.Anc = new(1, 2);
        await monitor.PollAsync([Device], Now.AddSeconds(2));
        Assert.Empty(changes);
    }

    [Fact]
    public async Task Poll_ReconnectedSessionUsesANewBaseline()
    {
        var session = new Session();
        var monitor = new EarbudMonitoringCoordinator(session);
        var changes = new List<AncObservation>();
        monitor.AncObserved += (_, observation) => { if (observation.ModeChanged) changes.Add(observation); };
        await monitor.PollAsync([Device], Now);
        await monitor.PollAsync([], Now.AddSeconds(2));
        session.Anc = new(2, 0);
        await monitor.PollAsync([Device], Now.AddSeconds(4));
        Assert.Equal(2, session.ConnectCalls);
        Assert.Empty(changes);
    }

    [Fact]
    public async Task Poll_FailedReadKeepsTheLastConfirmedBaseline()
    {
        var session = new Session();
        var monitor = new EarbudMonitoringCoordinator(session);
        var changes = new List<AncObservation>();
        monitor.AncObserved += (_, observation) => { if (observation.ModeChanged) changes.Add(observation); };
        await monitor.PollAsync([Device], Now);
        session.ReadFailure = true;
        await Assert.ThrowsAsync<IOException>(() => monitor.PollAsync([Device], Now.AddSeconds(2)));
        session.ReadFailure = false;
        session.Anc = new(2, 0);
        await monitor.PollAsync([Device], Now.AddSeconds(4));
        Assert.Single(changes);
    }

    [Fact]
    public async Task Poll_InvalidModeDoesNotOverwriteBaselineOrNotify()
    {
        var session = new Session();
        var monitor = new EarbudMonitoringCoordinator(session);
        var observations = new List<AncObservation>();
        monitor.AncObserved += (_, observation) => observations.Add(observation);
        await monitor.PollAsync([Device], Now);
        session.Anc = new(255, 0);
        await monitor.PollAsync([Device], Now.AddSeconds(2));
        Assert.Single(observations);
    }

    [Fact]
    public async Task Poll_FailedConnectionBacksOffInsteadOfRetryingEveryTwoSeconds()
    {
        var session = new Session { ConnectFailure = true };
        var monitor = new EarbudMonitoringCoordinator(session);
        await Assert.ThrowsAsync<IOException>(() => monitor.PollAsync([Device], Now));
        await monitor.PollAsync([Device], Now.AddSeconds(2));
        Assert.Equal(1, session.ConnectCalls);
        await Assert.ThrowsAsync<IOException>(() => monitor.PollAsync([Device], Now.AddSeconds(30)));
        Assert.Equal(2, session.ConnectCalls);
    }

    [Fact]
    public async Task Poll_ManualDisconnectStaysDisconnectedUntilWindowsReconnects()
    {
        var session = new Session();
        var monitor = new EarbudMonitoringCoordinator(session);
        await monitor.PollAsync([Device], Now);
        monitor.SuppressUntilWindowsReconnect(Device.BluetoothAddress);
        await session.DisconnectForMonitoringAsync();
        await monitor.PollAsync([Device], Now.AddMinutes(1));
        Assert.Equal(1, session.ConnectCalls);
        await monitor.PollAsync([], Now.AddMinutes(2));
        await monitor.PollAsync([Device], Now.AddMinutes(3));
        Assert.Equal(2, session.ConnectCalls);
    }

    [Fact]
    public async Task Poll_CancellationDoesNotStartAConnection()
    {
        var session = new Session();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new EarbudMonitoringCoordinator(session).PollAsync([Device], Now, cancellation.Token));
        Assert.Equal(0, session.ConnectCalls);
    }

    [Fact]
    public async Task Poll_OverlappingTicksDoNotQueueAdditionalReads()
    {
        var session = new Session { PendingRead = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var monitor = new EarbudMonitoringCoordinator(session);
        var first = monitor.PollAsync([Device], Now);
        await monitor.PollAsync([Device], Now.AddSeconds(2));
        Assert.Equal(1, session.ReadCalls);
        session.PendingRead.SetResult(new(1, 1));
        await first;
    }

    [Fact]
    public async Task Poll_UnknownModelsDoNotConnectOrNotify()
    {
        var session = new Session();
        var monitor = new EarbudMonitoringCoordinator(session);
        var notifications = new List<MotoBudsDevice>();
        monitor.WindowsConnected += (_, device) => notifications.Add(device);
        await monitor.PollAsync([Device with { ModelId = "Unknown" }], Now);
        Assert.Equal(0, session.ConnectCalls);
        Assert.Empty(notifications);
    }

    [Fact]
    public async Task Poll_ManagementReconnectAlsoResetsBaselineWithoutRepeatingWindowsNotification()
    {
        var session = new Session();
        var monitor = new EarbudMonitoringCoordinator(session);
        var connections = new List<MotoBudsDevice>();
        var changes = new List<AncObservation>();
        monitor.WindowsConnected += (_, device) => connections.Add(device);
        monitor.AncObserved += (_, observation) => { if (observation.ModeChanged) changes.Add(observation); };
        await monitor.PollAsync([Device], Now);
        await session.DisconnectForMonitoringAsync();
        session.Anc = new(0, 0);
        await monitor.PollAsync([Device], Now.AddSeconds(31));
        Assert.Equal(2, session.ConnectCalls);
        Assert.Single(connections);
        Assert.Empty(changes);
    }

    private sealed class Session : IEarbudMonitoringSession
    {
        public MotoBudsDevice? Device { get; private set; }
        public object? SessionIdentity { get; private set; }
        public bool IsConnected => Device is not null;
        public AncState Anc { get; set; } = new(1, 1);
        public bool ReadFailure { get; set; }
        public bool ConnectFailure { get; set; }
        public TaskCompletionSource<AncState>? PendingRead { get; init; }
        public int ConnectCalls { get; private set; }
        public int ReadCalls { get; private set; }
        public Task<bool> TryConnectForMonitoringAsync(MotoBudsDevice device, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ConnectCalls++;
            if (ConnectFailure) { throw new IOException("Connection failed."); }
            Device = device;
            SessionIdentity = new object();
            return Task.FromResult(true);
        }
        public Task<AncState> ReadAncStateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCalls++;
            return ReadFailure ? Task.FromException<AncState>(new IOException("Read failed."))
                : PendingRead?.Task.WaitAsync(cancellationToken) ?? Task.FromResult(Anc);
        }
        public Task DisconnectForMonitoringAsync()
        {
            Device = null;
            SessionIdentity = null;
            return Task.CompletedTask;
        }
    }
}
