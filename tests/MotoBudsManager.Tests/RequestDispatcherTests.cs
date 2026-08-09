using MotoBudsManager.Core;
using MotoBudsManager.Core.Models;
using MotoBudsManager.Core.Protocol;
using MotoBudsManager.Core.Transport;

namespace MotoBudsManager.Tests;

public sealed class RequestDispatcherTests
{
    [Fact]
    public async Task SendAsync_CorrelatesOpcodeAndSequence()
    {
        await using var transport = new FakeTransport(message =>
            new PduMessage(message.Opcode, PduType.ResponseNoAck, 0, message.Sequence, [0x7F]));
        await transport.ConnectAsync(CancellationToken.None);
        await using var dispatcher = new RequestDispatcher(transport, TimeSpan.FromSeconds(1));

        var response = await dispatcher.SendAsync(0x030D, new byte[] { 0x01 });

        Assert.Equal((ushort)0x030D, response.Opcode);
        Assert.Equal((ushort)0, response.Sequence);
        Assert.Equal(new byte[] { 0x7F }, response.Payload);
    }

    [Fact]
    public async Task SendAsync_SequenceWrapsAtUshortBoundary()
    {
        await using var transport = new FakeTransport(message =>
            new PduMessage(message.Opcode, PduType.ResponseNoAck, 0, message.Sequence, []));
        await transport.ConnectAsync(CancellationToken.None);
        await using var dispatcher = new RequestDispatcher(transport, TimeSpan.FromSeconds(1));
        dispatcher.SetNextSequenceForTesting(ushort.MaxValue);

        var first = await dispatcher.SendAsync(1, Array.Empty<byte>());
        var second = await dispatcher.SendAsync(2, Array.Empty<byte>());

        Assert.Equal(ushort.MaxValue, first.Sequence);
        Assert.Equal((ushort)0, second.Sequence);
    }

    [Fact]
    public async Task SendAsync_NoResponse_ThrowsTimeout()
    {
        await using var transport = new FakeTransport(_ => null);
        await transport.ConnectAsync(CancellationToken.None);
        await using var dispatcher = new RequestDispatcher(transport, TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAsync<TimeoutException>(() => dispatcher.SendAsync(1, Array.Empty<byte>()));
    }

    [Fact]
    public async Task SendAsync_TransportDisconnects_FailsImmediately()
    {
        await using var transport = new FakeTransport(_ => null);
        await transport.ConnectAsync(CancellationToken.None);
        await using var dispatcher = new RequestDispatcher(transport, TimeSpan.FromSeconds(5));

        var pending = dispatcher.SendAsync(1, Array.Empty<byte>());
        transport.EmitDisconnected();

        await Assert.ThrowsAsync<IOException>(() => pending.WaitAsync(TimeSpan.FromMilliseconds(250)));
    }

    [Fact]
    public async Task MessageReceived_ForwardsUnsolicitedBatteryNotification()
    {
        await using var transport = new FakeTransport(_ => null);
        await transport.ConnectAsync(CancellationToken.None);
        await using var dispatcher = new RequestDispatcher(transport, TimeSpan.FromSeconds(1));
        PduMessage? received = null;
        dispatcher.MessageReceived += (_, args) => received = args.Message;

        transport.Emit(new PduMessage(
            BatteryProtocol.BatteryLevelChangedOpcode,
            PduType.NotificationNoAck,
            0,
            0,
            [0x14, 0x17, 0x36]));

        Assert.NotNull(received);
        Assert.Equal(BatteryProtocol.BatteryLevelChangedOpcode, received.Opcode);
        Assert.Equal(new byte[] { 0x14, 0x17, 0x36 }, received.Payload);
    }

    [Fact]
    public async Task SetAncOn_AckThenReadback_ReturnsVerified()
    {
        var ancOn = false;
        await using var transport = new FakeTransport(message =>
        {
            if (message.Opcode == 0x0201)
            {
                ancOn = message.Payload.SequenceEqual(new byte[] { 1, 1 });
                return new(message.Opcode, PduType.ResponseNoAck, 0, message.Sequence, []);
            }

            return new(message.Opcode, PduType.ResponseNoAck, 0, message.Sequence,
                ancOn ? [0x01, 0x01] : [0x00, 0x03]);
        });
        await transport.ConnectAsync(CancellationToken.None);
        await using var dispatcher = new RequestDispatcher(transport, TimeSpan.FromSeconds(1));
        var device = new MotoBudsDevice("test", "moto buds", 0, "XT2443-1", 3, 0x10,
            TransportKind.Rfcomm, EvidenceLevel.HardwareVerified);
        var client = new MotoBudsClient(dispatcher, device, TimeSpan.Zero);

        var result = await client.SetAncOnAsync();

        Assert.True(result.Success);
        Assert.True(result.StateVerified);
    }

    [Fact]
    public async Task SetAncOff_StaleFirstReadback_PollsUntilVerified()
    {
        var readCount = 0;
        await using var transport = new FakeTransport(message =>
        {
            if (message.Opcode == 0x0201)
            {
                return new(message.Opcode, PduType.ResponseNoAck, 0, message.Sequence, []);
            }

            readCount++;
            return new(message.Opcode, PduType.ResponseNoAck, 0, message.Sequence,
                readCount == 1 ? [0x01, 0x01] : [0x00, 0x03]);
        });
        await transport.ConnectAsync(CancellationToken.None);
        await using var dispatcher = new RequestDispatcher(transport, TimeSpan.FromSeconds(1));
        var device = new MotoBudsDevice("test", "moto buds", 0, "XT2443-1", 3, 0x10,
            TransportKind.Rfcomm, EvidenceLevel.HardwareVerified);
        var client = new MotoBudsClient(dispatcher, device, TimeSpan.Zero);

        var result = await client.SetAncOffAsync();

        Assert.True(result.Success);
        Assert.True(readCount >= 2);
    }

    [Fact]
    public async Task GetBatteryState_HardwareVector_DecodesComponents()
    {
        await using var transport = new FakeTransport(message =>
            new(message.Opcode, PduType.ResponseNoAck, 0, message.Sequence, [0x14, 0x97, 0x36]));
        await transport.ConnectAsync(CancellationToken.None);
        await using var dispatcher = new RequestDispatcher(transport, TimeSpan.FromSeconds(1));
        var client = CreateXt2443Client(dispatcher);

        var state = await client.GetBatteryStateAsync();

        Assert.Equal(20, state.Left.Percent);
        Assert.False(state.Left.IsCharging);
        Assert.Equal(23, state.Right.Percent);
        Assert.True(state.Right.IsCharging);
        Assert.Equal(54, state.Case.Percent);
    }

    [Fact]
    public async Task SetAncTransparency_DynamicSubtype_ReadbackIsVerified()
    {
        var requests = new List<PduMessage>();
        await using var transport = new FakeTransport(message =>
        {
            requests.Add(message);
            return message.Opcode == 0x0201
                ? new(message.Opcode, PduType.ResponseNoAck, 0, message.Sequence, [])
                : new(message.Opcode, PduType.ResponseNoAck, 0, message.Sequence, [0x02, 0x01]);
        });
        await transport.ConnectAsync(CancellationToken.None);
        await using var dispatcher = new RequestDispatcher(transport, TimeSpan.FromSeconds(1));
        var client = CreateXt2443Client(dispatcher);

        var result = await client.SetAncModeAsync(AncModeValue.Transparency);

        Assert.True(result.Success);
        Assert.Equal((ushort)0x0201, requests[0].Opcode);
        Assert.Equal(new byte[] { 0x02, 0x00 }, requests[0].Payload);
        Assert.Equal((ushort)0x0200, requests[1].Opcode);
    }

    [Theory]
    [InlineData(Xt2443ToggleFeature.AdaptiveHearing, 0x0202, 0x0203)]
    [InlineData(Xt2443ToggleFeature.HiRes, 0x030C, 0x030D)]
    [InlineData(Xt2443ToggleFeature.GamingMode, 0x030E, 0x030F)]
    [InlineData(Xt2443ToggleFeature.VolumeBoost, 0x0313, 0x0314)]
    [InlineData(Xt2443ToggleFeature.InEarDetection, 0x0402, 0x0403)]
    public async Task SetXt2443Toggle_UsesHardwareVerifiedOpcodePair(
        Xt2443ToggleFeature feature,
        ushort getOpcode,
        ushort setOpcode)
    {
        var requests = new List<PduMessage>();
        await using var transport = new FakeTransport(message =>
        {
            requests.Add(message);
            return new(message.Opcode, PduType.ResponseNoAck, 0, message.Sequence,
                message.Opcode == getOpcode ? [0x01, 0x01] : []);
        });
        await transport.ConnectAsync(CancellationToken.None);
        await using var dispatcher = new RequestDispatcher(transport, TimeSpan.FromSeconds(1));
        var client = CreateXt2443Client(dispatcher);

        var result = await client.SetXt2443ToggleAsync(feature, true);

        Assert.True(result.Success);
        Assert.Equal(setOpcode, requests[0].Opcode);
        Assert.Equal(new byte[] { 0x01 }, requests[0].Payload);
        Assert.Equal(getOpcode, requests[1].Opcode);
    }

    [Fact]
    public async Task SetDualConnection_PreservesSupportByteAndReadsBack()
    {
        var enabled = false;
        var requests = new List<PduMessage>();
        await using var transport = new FakeTransport(message =>
        {
            requests.Add(message);
            if (message.Opcode == 0x0407)
            {
                enabled = message.Payload.SequenceEqual(new byte[] { 0x01, 0x01 });
                return new(message.Opcode, PduType.ResponseNoAck, 0, message.Sequence, []);
            }

            return new(message.Opcode, PduType.ResponseNoAck, 0, message.Sequence,
                [0x01, (byte)(enabled ? 1 : 0)]);
        });
        await transport.ConnectAsync(CancellationToken.None);
        await using var dispatcher = new RequestDispatcher(transport, TimeSpan.FromSeconds(1));
        var client = CreateXt2443Client(dispatcher);

        var result = await client.SetDualConnectionAsync(true);

        Assert.True(result.Success);
        Assert.Equal(new byte[] { 0x01, 0x01 }, requests.Single(message => message.Opcode == 0x0407).Payload);
        Assert.Equal(2, requests.Count(message => message.Opcode == 0x0406));
    }

    [Fact]
    public async Task ActionCommands_UseObservedFindAndFitPayloads()
    {
        var requests = new List<PduMessage>();
        await using var transport = new FakeTransport(message =>
        {
            requests.Add(message);
            return new(message.Opcode, PduType.ResponseNoAck, 0, message.Sequence, []);
        });
        await transport.ConnectAsync(CancellationToken.None);
        await using var dispatcher = new RequestDispatcher(transport, TimeSpan.FromSeconds(1));
        var client = CreateXt2443Client(dispatcher);

        Assert.True((await client.SetRingTargetAsync(EarbudRingTarget.Left)).Success);
        Assert.True(client.HasTransientActions);
        Assert.True((await client.SetRingTargetAsync(EarbudRingTarget.Right)).Success);
        Assert.True((await client.SetRingTargetAsync(EarbudRingTarget.Stop)).Success);
        Assert.False(client.HasTransientActions);
        Assert.True((await client.SetFitTestAsync(true)).Success);
        Assert.True(client.HasTransientActions);
        client.CompleteFitTest();
        Assert.False(client.HasTransientActions);
        Assert.True((await client.SetFitTestAsync(true)).Success);
        Assert.True((await client.SetFitTestAsync(false)).Success);
        Assert.False(client.HasTransientActions);

        Assert.Equal(new byte[] { 0x01, 0x00 }, requests[0].Payload);
        Assert.Equal(new byte[] { 0x00, 0x01 }, requests[1].Payload);
        Assert.Equal(new byte[] { 0x00, 0x00 }, requests[2].Payload);
        Assert.Equal(new byte[] { 0x01 }, requests[3].Payload);
        Assert.Equal(new byte[] { 0x01 }, requests[4].Payload);
        Assert.Equal(new byte[] { 0x00 }, requests[5].Payload);
    }

    [Theory]
    [InlineData(ExperimentalFeature.SpatialAudio, 2, 0x030A, 0x030B, "01 01")]
    [InlineData(ExperimentalFeature.HiRes, 1, 0x030C, 0x030D, "01")]
    [InlineData(ExperimentalFeature.GamingMode, 1, 0x030E, 0x030F, "01")]
    [InlineData(ExperimentalFeature.BassBoost, 1, 0x031C, 0x031D, "01")]
    [InlineData(ExperimentalFeature.InEarDetection, 1, 0x0402, 0x0403, "01")]
    public async Task SetFeature_StaticModel_UsesWriteThenReadback(
        ExperimentalFeature feature,
        int value,
        ushort getOpcode,
        ushort setOpcode,
        string readbackHex)
    {
        var requests = new List<PduMessage>();
        await using var transport = new FakeTransport(message =>
        {
            requests.Add(message);
            return new(message.Opcode, PduType.ResponseNoAck, 0, message.Sequence,
                message.Opcode == getOpcode ? ProtocolCodecTests.Hex(readbackHex) : []);
        });
        await transport.ConnectAsync(CancellationToken.None);
        await using var dispatcher = new RequestDispatcher(transport, TimeSpan.FromSeconds(1));
        var client = new MotoBudsClient(dispatcher,
            new MotoBudsDevice("test", "moto buds plus", 0, "XT2641-1", 1, 0x10,
                TransportKind.Rfcomm, EvidenceLevel.StaticAnalysis), TimeSpan.Zero);

        var result = await client.SetFeatureAsync(feature, value);

        Assert.True(result.Success);
        Assert.Equal(setOpcode, requests[0].Opcode);
        Assert.Equal(getOpcode, requests[1].Opcode);
        Assert.True(result.StateVerified);
    }

    private static MotoBudsClient CreateXt2443Client(RequestDispatcher dispatcher) =>
        new(dispatcher,
            new MotoBudsDevice("test", "moto buds", 0, "XT2443-1", 3, 0x10,
                TransportKind.Rfcomm, EvidenceLevel.HardwareVerified),
            TimeSpan.Zero);

    private sealed class FakeTransport(Func<PduMessage, PduMessage?> responder) : IMotoBudsTransport
    {
        public event EventHandler<PduReceivedEventArgs>? PduReceived;

        public event EventHandler? Disconnected;

        public TransportKind Kind => TransportKind.Rfcomm;

        public bool IsConnected { get; private set; }

        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task SendPduAsync(ReadOnlyMemory<byte> pdu, CancellationToken cancellationToken)
        {
            var request = ProtocolCodec.ParsePdu(pdu.Span);
            var response = responder(request);
            if (response is not null)
            {
                PduReceived?.Invoke(this, new(response));
            }

            return Task.CompletedTask;
        }

        public Task DisconnectAsync()
        {
            IsConnected = false;
            Disconnected?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public void Emit(PduMessage message) => PduReceived?.Invoke(this, new(message));

        public void EmitDisconnected()
        {
            IsConnected = false;
            Disconnected?.Invoke(this, EventArgs.Empty);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
