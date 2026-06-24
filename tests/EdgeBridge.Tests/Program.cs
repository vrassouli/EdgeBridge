using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using EdgeBridge.Abstractions;
using EdgeBridge.Agent;
using EdgeBridge.Client;
using EdgeBridge.Protocol;
using EdgeBridge.Transport.WebSockets;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Protocol command request round-trips with payload", ProtocolCommandRequestRoundTripsAsync),
    ("Mock device mirrors digital output to input watchers", MockDeviceMirrorsDigitalOutputAsync),
    ("Remote device correlates command responses", RemoteDeviceCorrelatesCommandResponsesAsync),
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

static async Task RemoteWatchSendsUnsubscribeAsync()
{
    var transport = new FakeTransport();
    var device = await StartRemoteDeviceAsync(transport);
    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));

    await using var watcher = device.DigitalInput(3).WatchAsync(cancellation.Token).GetAsyncEnumerator(cancellation.Token);
    var moveTask = watcher.MoveNextAsync().AsTask();
    var subscribe = AssertIs<SubscribeRequest>(await transport.Current.ExpectSentAsync());
    AssertEqual(EdgeBridgeCommands.DigitalWatch, subscribe.Event);

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
