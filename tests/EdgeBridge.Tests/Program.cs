using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using EdgeBridge.Abstractions;
using EdgeBridge.Agent;
using EdgeBridge.Client;
using EdgeBridge.Protocol;
using EdgeBridge.Samples.Avalonia.Models;
using EdgeBridge.Transport.WebSockets;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Protocol command request round-trips with payload", ProtocolCommandRequestRoundTripsAsync),
    ("Profile JSON round-trips", ProfileJsonRoundTripsAsync),
    ("Agent config update persists and requires restart", AgentConfigUpdatePersistsAsync),
    ("Mock device mirrors digital output to input watchers", MockDeviceMirrorsDigitalOutputAsync),
    ("Mock device supports I2C and camera controls", MockDeviceSupportsI2cAndCameraAsync),
    ("Remote device correlates command responses", RemoteDeviceCorrelatesCommandResponsesAsync),
    ("Remote device sends I2C, camera, and config commands", RemoteDeviceSendsManagementCommandsAsync),
    ("Remote digital watch sends unsubscribe on disposal", RemoteWatchSendsUnsubscribeAsync),
    ("Remote device reports health and reconnects", RemoteDeviceReportsHealthAndReconnectsAsync)
};

var failed = 0;

foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {test.Name}");
        Console.Error.WriteLine(ex);
    }
}

if (failed > 0)
{
    Environment.ExitCode = 1;
}

static Task ProtocolCommandRequestRoundTripsAsync()
{
    var request = new CommandRequest
    {
        Type = MessageTypes.CommandRequest,
        DeviceId = "device-1",
        Command = EdgeBridgeCommands.DigitalWrite,
        Payload = ProtocolJson.ToJsonElement(new DigitalWritePayload(17, true))
    };

    var serialized = WebSocketMessageSerializer.Serialize(request);
    var roundTrip = AssertIs<CommandRequest>(WebSocketMessageSerializer.Deserialize(serialized));
    var payload = ProtocolJson.ReadPayload<DigitalWritePayload>(roundTrip.Payload);

    AssertEqual(MessageTypes.CommandRequest, roundTrip.Type);
    AssertEqual(EdgeBridgeCommands.DigitalWrite, roundTrip.Command);
    AssertEqual(17, payload?.Channel);
    AssertEqual(true, payload?.IsHigh);

    return Task.CompletedTask;
}

static Task ProfileJsonRoundTripsAsync()
{
    var store = new DeviceProfileStore
    {
        Devices =
        {
            new DeviceProfile
            {
                Name = "Bench Device",
                Endpoint = "ws://bench.local:8080/edgebridge/",
                GpioChannels =
                {
                    new GpioChannelProfile
                    {
                        Name = "Limit Switch",
                        Channel = 4,
                        Direction = GpioDirection.Input,
                        PullMode = DigitalInputPullMode.PullDown,
                        LastValue = true
                    }
                }
            }
        }
    };

    var json = JsonSerializer.Serialize(store, ProtocolJson.Options);
    var roundTrip = JsonSerializer.Deserialize<DeviceProfileStore>(json, ProtocolJson.Options);

    AssertEqual("Bench Device", roundTrip?.Devices[0].Name);
    AssertEqual("ws://bench.local:8080/edgebridge/", roundTrip?.Devices[0].Endpoint);
    AssertEqual(GpioDirection.Input, roundTrip?.Devices[0].GpioChannels.Last().Direction);
    AssertEqual(DigitalInputPullMode.PullDown, roundTrip?.Devices[0].GpioChannels.Last().PullMode);
    AssertEqual(true, roundTrip?.Devices[0].GpioChannels.Last().LastValue);
    return Task.CompletedTask;
}

static async Task AgentConfigUpdatePersistsAsync()
{
    var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"edgebridge-agent-{Guid.NewGuid():n}.json");
    try
    {
        var store = await AgentConfigStore.LoadAsync([$"--config={tempPath}"], CancellationToken.None);
        var result = await store.UpdateAsync(new AgentConfigDto
        {
            DeviceId = "device-2",
            DeviceName = "Device Two",
            Modules = new AgentModuleConfigDto { Gpio = true, Pwm = true, I2c = true, Camera = true },
            Hardware = new AgentHardwareConfigDto
            {
                Backend = HardwareBackends.Mock,
                PwmFrequency = 1000,
                I2cDevices = [new AgentI2cDeviceConfigDto { Name = "Sensor", Bus = 1, Address = 0x40 }],
                Cameras = [new AgentCameraConfigDto { CameraId = "camera0", Enabled = true }]
            }
        }, CancellationToken.None);

        AssertTrue(result.Accepted);
        AssertTrue(result.RestartRequired);
        AssertTrue(File.Exists(tempPath));

        var persisted = JsonSerializer.Deserialize<AgentConfig>(
            await File.ReadAllTextAsync(tempPath),
            ProtocolJson.Options);
        AssertEqual("device-2", persisted?.DeviceId);
        AssertEqual(true, persisted?.Modules.I2c);
        AssertEqual("camera0", persisted?.Hardware.Cameras[0].CameraId);
    }
    finally
    {
        File.Delete(tempPath);
    }
}

static async Task MockDeviceMirrorsDigitalOutputAsync()
{
    var device = new MockDevice(new AgentConfig());
    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));

    var input = device.DigitalInput(4);
    var output = device.DigitalOutput(4);
    var readTask = input.WatchAsync(cancellation.Token).GetAsyncEnumerator(cancellation.Token);

    await output.SetAsync(true, cancellation.Token);

    try
    {
        AssertTrue(await readTask.MoveNextAsync());
        AssertEqual(4, readTask.Current.Channel.Number);
        AssertEqual(true, readTask.Current.IsHigh);
        var current = await input.ReadAsync(cancellation.Token);
        AssertEqual(true, current.IsHigh);
    }
    finally
    {
        await readTask.DisposeAsync();
    }
}

static async Task MockDeviceSupportsI2cAndCameraAsync()
{
    var device = new MockDevice(new AgentConfig
    {
        Modules = new ModuleConfig { Gpio = true, Pwm = true, I2c = true, Camera = true }
    });
    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));

    var i2c = device.I2cDevice(1, 0x40);
    await i2c.WriteRegisterAsync(0x10, new byte[] { 0xAB, 0xCD }, cancellation.Token);
    var read = await i2c.ReadRegisterAsync(0x10, 2, cancellation.Token);
    AssertEqual(0xAB, read[0]);
    AssertEqual(0xCD, read[1]);

    var camera = device.Camera("camera0");
    await camera.StartStreamAsync(cancellation.Token);
    AssertEqual(true, (await camera.GetStatusAsync(cancellation.Token)).IsStreaming);
    await camera.StopStreamAsync(cancellation.Token);
    AssertEqual(false, (await camera.GetStatusAsync(cancellation.Token)).IsStreaming);
}

static async Task RemoteDeviceCorrelatesCommandResponsesAsync()
{
    var transport = new FakeTransport();
    var device = new RemoteDevice(new Uri("ws://edgebridge.test/"), transport);
    var startTask = device.StartAsync().AsTask();

    var getInfo = AssertIs<CommandRequest>(await transport.Current.ExpectSentAsync());
    AssertEqual(EdgeBridgeCommands.DeviceGetInfo, getInfo.Command);
    await transport.Current.InjectAsync(Success(getInfo, new DeviceInfoPayload(
        new DeviceInfo("remote-1", "Remote One", "fake", ["gpio.digital"]))));
    await startTask;

    var setTask = device.DigitalOutput(8).SetAsync(true).AsTask();
    var write = AssertIs<CommandRequest>(await transport.Current.ExpectSentAsync());
    AssertEqual(EdgeBridgeCommands.DigitalWrite, write.Command);
    await transport.Current.InjectAsync(Success(write, new { ok = true }));
    await setTask;

    await device.DisposeAsync();
}

static async Task RemoteDeviceSendsManagementCommandsAsync()
{
    var transport = new FakeTransport();
    var device = await StartRemoteDeviceAsync(transport);

    var writeTask = device.I2cDevice(1, 0x40).WriteRegisterAsync(0x10, new byte[] { 0x01, 0x02 }).AsTask();
    var write = AssertIs<CommandRequest>(await transport.Current.ExpectSentAsync());
    AssertEqual(EdgeBridgeCommands.I2cWriteRegister, write.Command);
    await transport.Current.InjectAsync(Success(write, new { ok = true }));
    await writeTask;

    var readTask = device.I2cDevice(1, 0x40).ReadRegisterAsync(0x10, 2).AsTask();
    var read = AssertIs<CommandRequest>(await transport.Current.ExpectSentAsync());
    AssertEqual(EdgeBridgeCommands.I2cReadRegister, read.Command);
    await transport.Current.InjectAsync(Success(read, new I2cReadRegisterResult(
        1,
        0x40,
        0x10,
        [0x01, 0x02],
        DateTimeOffset.UtcNow)));
    AssertEqual(2, (await readTask).Length);

    var startTask = device.Camera("camera0").StartStreamAsync().AsTask();
    var start = AssertIs<CommandRequest>(await transport.Current.ExpectSentAsync());
    AssertEqual(EdgeBridgeCommands.CameraStartStream, start.Command);
    await transport.Current.InjectAsync(Success(start, new { ok = true }));
    await startTask;

    var configTask = ((IAgentConfigurationClient)device).GetAgentConfigAsync().AsTask();
    var config = AssertIs<CommandRequest>(await transport.Current.ExpectSentAsync());
    AssertEqual(EdgeBridgeCommands.DeviceConfigGet, config.Command);
    await transport.Current.InjectAsync(Success(config, new AgentConfigPayload(new AgentConfigDto
    {
        DeviceId = "remote-1",
        DeviceName = "Remote One"
    })));
    AssertEqual("remote-1", (await configTask).DeviceId);

    await device.DisposeAsync();
}

static async Task RemoteWatchSendsUnsubscribeAsync()
{
    var transport = new FakeTransport();
    var device = await StartRemoteDeviceAsync(transport);
    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));

    await using var watcher = device
        .DigitalInput(3, new DigitalInputOptions(DigitalInputPullMode.PullDown))
        .WatchAsync(cancellation.Token)
        .GetAsyncEnumerator(cancellation.Token);
    var moveTask = watcher.MoveNextAsync().AsTask();
    var subscribe = AssertIs<SubscribeRequest>(await transport.Current.ExpectSentAsync());
    AssertEqual(EdgeBridgeCommands.DigitalWatch, subscribe.Event);
    var watchPayload = ProtocolJson.ReadPayload<DigitalWatchPayload>(subscribe.Payload);
    AssertEqual(DigitalInputPullMode.PullDown, watchPayload?.PullMode);

    await transport.Current.InjectAsync(new EventMessage
    {
        Type = MessageTypes.Event,
        Event = EdgeBridgeEvents.DigitalInputChanged,
        SubscriptionId = subscribe.CorrelationId,
        Payload = ProtocolJson.ToJsonElement(new DigitalReadResult(3, true, DateTimeOffset.UtcNow))
    });

    AssertTrue(await moveTask);
    AssertEqual(true, watcher.Current.IsHigh);

    await watcher.DisposeAsync();
    var unsubscribe = AssertIs<UnsubscribeRequest>(await transport.Current.ExpectSentAsync());
    AssertEqual(subscribe.CorrelationId, unsubscribe.SubscriptionId);

    await device.DisposeAsync();
}

static async Task RemoteDeviceReportsHealthAndReconnectsAsync()
{
    var transport = new FakeTransport();
    var device = await StartRemoteDeviceAsync(transport);

    AssertEqual(EdgeConnectionState.Connected, device.Health.State);

    await transport.Current.InjectAsync(new HeartbeatMessage
    {
        Type = MessageTypes.Heartbeat,
        DeviceId = "remote-1",
        Runtime = "fake"
    });

    await WaitUntilAsync(() => device.Health.LastHeartbeatAt is not null);

    await device.ReconnectAsync();
    AssertEqual(2, transport.ConnectCount);
    AssertEqual(EdgeConnectionState.Connected, device.Health.State);

    await device.DisposeAsync();
}

static async Task<RemoteDevice> StartRemoteDeviceAsync(FakeTransport transport)
{
    var device = new RemoteDevice(new Uri("ws://edgebridge.test/"), transport);
    var startTask = device.StartAsync().AsTask();
    var getInfo = AssertIs<CommandRequest>(await transport.Current.ExpectSentAsync());
    await transport.Current.InjectAsync(Success(getInfo, new DeviceInfoPayload(
        new DeviceInfo("remote-1", "Remote One", "fake", ["gpio.digital"]))));
    await startTask;
    return device;
}

static CommandResponse Success<TPayload>(CommandRequest request, TPayload payload)
{
    return new CommandResponse
    {
        Type = MessageTypes.CommandResponse,
        CorrelationId = request.MessageId,
        Success = true,
        Payload = ProtocolJson.ToJsonElement(payload)
    };
}

static async Task WaitUntilAsync(Func<bool> condition)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));

    while (!condition())
    {
        await Task.Delay(10, timeout.Token);
    }
}

static T AssertIs<T>(object? value)
{
    if (value is T typed)
    {
        return typed;
    }

    throw new InvalidOperationException($"Expected {typeof(T).Name}, got {value?.GetType().Name ?? "null"}.");
}

static void AssertEqual<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected {JsonSerializer.Serialize(expected)}, got {JsonSerializer.Serialize(actual)}.");
    }
}

static void AssertTrue(bool value)
{
    if (!value)
    {
        throw new InvalidOperationException("Expected true.");
    }
}

internal sealed class FakeTransport : ITransport
{
    private readonly ConcurrentQueue<FakeConnection> _connections = new();

    public int ConnectCount { get; private set; }

    public FakeConnection Current { get; private set; } = new();

    public ValueTask<ITransportConnection> ConnectAsync(Uri endpoint, CancellationToken cancellationToken = default)
    {
        ConnectCount++;
        Current = new FakeConnection();
        _connections.Enqueue(Current);
        return ValueTask.FromResult<ITransportConnection>(Current);
    }
}

internal sealed class FakeConnection : ITransportConnection
{
    private readonly Channel<ProtocolMessage> _incoming = Channel.CreateUnbounded<ProtocolMessage>();
    private readonly Channel<ProtocolMessage> _sent = Channel.CreateUnbounded<ProtocolMessage>();

    public ValueTask SendAsync(ProtocolMessage message, CancellationToken cancellationToken = default)
    {
        return _sent.Writer.WriteAsync(message, cancellationToken);
    }

    public async IAsyncEnumerable<ProtocolMessage> ReceiveAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var message in _incoming.Reader.ReadAllAsync(cancellationToken))
        {
            yield return message;
        }
    }

    public ValueTask InjectAsync(ProtocolMessage message)
    {
        return _incoming.Writer.WriteAsync(message);
    }

    public ValueTask<ProtocolMessage> ExpectSentAsync()
    {
        return _sent.Reader.ReadAsync();
    }

    public ValueTask DisposeAsync()
    {
        _incoming.Writer.TryComplete();
        _sent.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
