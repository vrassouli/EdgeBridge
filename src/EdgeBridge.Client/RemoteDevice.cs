using System.Collections.Concurrent;
using System.Threading.Channels;
using EdgeBridge.Abstractions;
using EdgeBridge.Protocol;
using EdgeBridge.Transport.WebSockets;

namespace EdgeBridge.Client;

internal sealed class RemoteDevice : IDevice, IRemoteDeviceConnection, IAgentConfigurationClient, IAsyncDisposable
{
    private static readonly TimeSpan[] ReconnectDelays =
    [
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5)
    ];

    private readonly Uri _endpoint;
    private readonly ITransport _transport;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<CommandResponse>> _pending = new();
    private readonly ConcurrentDictionary<string, Channel<DigitalInputState>> _digitalWatchers = new();
    private readonly CancellationTokenSource _disposeCts = new();
    private ITransportConnection? _connection;
    private CancellationTokenSource? _receiveCts;
    private Task? _receiveTask;
    private bool _disposed;

    public RemoteDevice(Uri endpoint, ITransport transport)
    {
        _endpoint = endpoint;
        _transport = transport;
    }

    public string DeviceId { get; private set; } = "remote";

    public EdgeConnectionHealth Health { get; private set; } = new(EdgeConnectionState.Closed);

    public event EventHandler<EdgeConnectionHealth>? HealthChanged;

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        await ConnectTransportAsync(EdgeConnectionState.Connecting, cancellationToken).ConfigureAwait(false);
        var info = await GetInfoAsync(cancellationToken).ConfigureAwait(false);
        DeviceId = info.DeviceId;
    }

    public ValueTask ReconnectAsync(CancellationToken cancellationToken = default)
    {
        return ConnectTransportAsync(EdgeConnectionState.Reconnecting, cancellationToken);
    }

    public async ValueTask<DeviceInfo> GetInfoAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendCommandAsync(EdgeBridgeCommands.DeviceGetInfo, new { }, cancellationToken).ConfigureAwait(false);
        var payload = ProtocolJson.ReadPayload<DeviceInfoPayload>(response.Payload);
        return payload?.Device ?? throw new InvalidOperationException("Agent returned an empty device info payload.");
    }

    public IDigitalOutput DigitalOutput(int channel) => new RemoteDigitalOutput(this, channel);

    public IDigitalInput DigitalInput(int channel) => DigitalInput(channel, DigitalInputOptions.Default);

    public IDigitalInput DigitalInput(int channel, DigitalInputOptions options) => new RemoteDigitalInput(this, channel, options);

    public IPwmOutput PwmOutput(int channel) => new RemotePwmOutput(this, channel);

    public IMotor Motor(string name) => new RemoteMotor(this, name);

    public II2cDevice I2cDevice(int bus, int address) => new RemoteI2cDevice(this, bus, address);

    public ICamera Camera(string cameraId) => new RemoteCamera(this, cameraId);

    public async ValueTask<AgentConfigDto> GetAgentConfigAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendCommandAsync(EdgeBridgeCommands.DeviceConfigGet, new { }, cancellationToken).ConfigureAwait(false);
        var payload = ProtocolJson.ReadPayload<AgentConfigPayload>(response.Payload);
        return payload?.Config ?? throw new InvalidOperationException("Agent returned an empty config payload.");
    }

    public async ValueTask<AgentConfigUpdateResult> UpdateAgentConfigAsync(
        AgentConfigDto config,
        CancellationToken cancellationToken = default)
    {
        var response = await SendCommandAsync(
            EdgeBridgeCommands.DeviceConfigUpdate,
            new AgentConfigPayload(config),
            cancellationToken).ConfigureAwait(false);
        return ProtocolJson.ReadPayload<AgentConfigUpdateResult>(response.Payload)
            ?? throw new InvalidOperationException("Agent returned an empty config update payload.");
    }

    internal ValueTask SetDigitalOutputAsync(int channel, bool isHigh, CancellationToken cancellationToken)
    {
        return SendCommandWithoutResultAsync(
            EdgeBridgeCommands.DigitalWrite,
            new DigitalWritePayload(channel, isHigh),
            cancellationToken);
    }

    internal async ValueTask<DigitalInputState> ReadDigitalInputAsync(
        int channel,
        DigitalInputOptions options,
        CancellationToken cancellationToken)
    {
        var response = await SendCommandAsync(
            EdgeBridgeCommands.DigitalRead,
            new DigitalReadPayload(channel, options.PullMode),
            cancellationToken).ConfigureAwait(false);
        var result = ProtocolJson.ReadPayload<DigitalReadResult>(response.Payload)
            ?? throw new InvalidOperationException("Agent returned an empty digital read payload.");

        return new DigitalInputState(new DigitalChannel(result.Channel), result.IsHigh, result.Timestamp);
    }

    internal async IAsyncEnumerable<DigitalInputState> WatchDigitalInputAsync(
        int channel,
        DigitalInputOptions options,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var subscriptionId = $"digital:{channel}:{Guid.NewGuid():n}";
        var watcher = Channel.CreateUnbounded<DigitalInputState>();
        _digitalWatchers[subscriptionId] = watcher;

        var request = new SubscribeRequest
        {
            Type = MessageTypes.SubscribeRequest,
            DeviceId = DeviceId,
            Event = EdgeBridgeCommands.DigitalWatch,
            Payload = ProtocolJson.ToJsonElement(new DigitalWatchPayload(channel, options.PullMode)),
            CorrelationId = subscriptionId
        };

        await SendMessageAsync(request, cancellationToken).ConfigureAwait(false);

        try
        {
            await foreach (var state in watcher.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return state;
            }
        }
        finally
        {
            _digitalWatchers.TryRemove(subscriptionId, out _);
            watcher.Writer.TryComplete();

            try
            {
                await SendMessageAsync(new UnsubscribeRequest
                {
                    Type = MessageTypes.UnsubscribeRequest,
                    DeviceId = DeviceId,
                    SubscriptionId = subscriptionId
                }, CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // The connection may already be gone; remote cleanup also happens on disconnect.
            }
        }
    }

    internal ValueTask SetPwmAsync(int channel, double dutyCycle, CancellationToken cancellationToken)
    {
        return SendCommandWithoutResultAsync(EdgeBridgeCommands.PwmSet, new PwmSetPayload(channel, dutyCycle), cancellationToken);
    }

    internal ValueTask SetMotorSpeedAsync(string name, double speed, CancellationToken cancellationToken)
    {
        return SendCommandWithoutResultAsync(EdgeBridgeCommands.MotorSetSpeed, new MotorSetSpeedPayload(name, speed), cancellationToken);
    }

    internal async ValueTask<byte[]> ReadI2cRegisterAsync(
        int bus,
        int address,
        int register,
        int length,
        CancellationToken cancellationToken)
    {
        var response = await SendCommandAsync(
            EdgeBridgeCommands.I2cReadRegister,
            new I2cReadRegisterPayload(bus, address, register, length),
            cancellationToken).ConfigureAwait(false);
        var result = ProtocolJson.ReadPayload<I2cReadRegisterResult>(response.Payload)
            ?? throw new InvalidOperationException("Agent returned an empty I2C read payload.");
        return result.Data;
    }

    internal ValueTask WriteI2cRegisterAsync(
        int bus,
        int address,
        int register,
        IReadOnlyList<byte> data,
        CancellationToken cancellationToken)
    {
        return SendCommandWithoutResultAsync(
            EdgeBridgeCommands.I2cWriteRegister,
            new I2cWriteRegisterPayload(bus, address, register, data.ToArray()),
            cancellationToken);
    }

    internal ValueTask StartCameraAsync(string cameraId, CancellationToken cancellationToken)
    {
        return SendCommandWithoutResultAsync(
            EdgeBridgeCommands.CameraStartStream,
            new CameraStreamPayload(cameraId),
            cancellationToken);
    }

    internal ValueTask StopCameraAsync(string cameraId, CancellationToken cancellationToken)
    {
        return SendCommandWithoutResultAsync(
            EdgeBridgeCommands.CameraStopStream,
            new CameraStreamPayload(cameraId),
            cancellationToken);
    }

    internal async ValueTask<CameraStatus> GetCameraStatusAsync(
        string cameraId,
        CancellationToken cancellationToken)
    {
        var response = await SendCommandAsync(
            EdgeBridgeCommands.CameraGetStatus,
            new CameraStreamPayload(cameraId),
            cancellationToken).ConfigureAwait(false);
        var result = ProtocolJson.ReadPayload<CameraStatusPayload>(response.Payload)
            ?? throw new InvalidOperationException("Agent returned an empty camera status payload.");
        return result.Status;
    }

    private async ValueTask ConnectTransportAsync(EdgeConnectionState connectingState, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SetHealth(connectingState);

            _receiveCts?.Cancel();

            if (_connection is not null)
            {
                await _connection.DisposeAsync().ConfigureAwait(false);
            }

            _receiveCts?.Dispose();
            _connection = await _transport.ConnectAsync(_endpoint, cancellationToken).ConfigureAwait(false);
            _receiveCts = CancellationTokenSource.CreateLinkedTokenSource(_disposeCts.Token);
            _receiveTask = Task.Run(() => ReceiveLoopAsync(_connection, _receiveCts.Token), CancellationToken.None);
            SetHealth(EdgeConnectionState.Connected);
        }
        catch (Exception ex)
        {
            SetHealth(EdgeConnectionState.Faulted, ex.Message);
            throw;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private async Task ReconnectLoopAsync()
    {
        for (var attempt = 0; attempt < ReconnectDelays.Length && !_disposeCts.IsCancellationRequested; attempt++)
        {
            try
            {
                SetHealth(EdgeConnectionState.Reconnecting);
                await Task.Delay(ReconnectDelays[attempt], _disposeCts.Token).ConfigureAwait(false);
                await ConnectTransportAsync(EdgeConnectionState.Reconnecting, _disposeCts.Token).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (_disposeCts.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                SetHealth(EdgeConnectionState.Faulted, ex.Message);
            }
        }
    }

    private async ValueTask SendCommandWithoutResultAsync<TPayload>(
        string command,
        TPayload payload,
        CancellationToken cancellationToken)
    {
        await SendCommandAsync(command, payload, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<CommandResponse> SendCommandAsync<TPayload>(
        string command,
        TPayload payload,
        CancellationToken cancellationToken)
    {
        var request = new CommandRequest
        {
            Type = MessageTypes.CommandRequest,
            DeviceId = DeviceId,
            Command = command,
            Payload = ProtocolJson.ToJsonElement(payload)
        };

        var pending = new TaskCompletionSource<CommandResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[request.MessageId] = pending;

        try
        {
            await SendMessageAsync(request, cancellationToken).ConfigureAwait(false);

            await using var registration = cancellationToken.Register(() => pending.TrySetCanceled(cancellationToken));
            var response = await pending.Task.ConfigureAwait(false);

            if (!response.Success)
            {
                throw new InvalidOperationException(response.Error?.Message ?? "EdgeBridge command failed.");
            }

            return response;
        }
        catch
        {
            _pending.TryRemove(request.MessageId, out _);
            throw;
        }
    }

    private async ValueTask SendMessageAsync(ProtocolMessage message, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var connection = _connection ?? throw new InvalidOperationException("EdgeBridge device is not connected.");
        await connection.SendAsync(message, cancellationToken).ConfigureAwait(false);
    }

    private async Task ReceiveLoopAsync(ITransportConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in connection.ReceiveAsync(cancellationToken).ConfigureAwait(false))
            {
                RecordReceivedMessage(message);

                switch (message)
                {
                    case CommandResponse response when response.CorrelationId is not null:
                        if (_pending.TryRemove(response.CorrelationId, out var pending))
                        {
                            pending.TrySetResult(response);
                        }
                        break;
                    case EventMessage { Event: EdgeBridgeEvents.DigitalInputChanged } eventMessage:
                        DispatchDigitalInputEvent(eventMessage);
                        break;
                }
            }

            if (!_disposed && !cancellationToken.IsCancellationRequested)
            {
                SetHealth(EdgeConnectionState.Faulted, "Connection closed.");
                FailPendingCommands(new IOException("EdgeBridge connection closed."));
                await ReconnectLoopAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            FailPendingCommands(ex);
            SetHealth(EdgeConnectionState.Faulted, ex.Message);

            if (!_disposed && !cancellationToken.IsCancellationRequested)
            {
                await ReconnectLoopAsync().ConfigureAwait(false);
            }
        }
    }

    private void RecordReceivedMessage(ProtocolMessage message)
    {
        var lastHeartbeat = message is HeartbeatMessage
            ? DateTimeOffset.UtcNow
            : Health.LastHeartbeatAt;

        SetHealth(EdgeConnectionState.Connected, lastMessageAt: DateTimeOffset.UtcNow, lastHeartbeatAt: lastHeartbeat);
    }

    private void DispatchDigitalInputEvent(EventMessage eventMessage)
    {
        if (eventMessage.SubscriptionId is null ||
            !_digitalWatchers.TryGetValue(eventMessage.SubscriptionId, out var watcher))
        {
            return;
        }

        var payload = ProtocolJson.ReadPayload<DigitalReadResult>(eventMessage.Payload);
        if (payload is null)
        {
            return;
        }

        watcher.Writer.TryWrite(new DigitalInputState(
            new DigitalChannel(payload.Channel),
            payload.IsHigh,
            payload.Timestamp));
    }

    private void FailPendingCommands(Exception exception)
    {
        foreach (var pending in _pending)
        {
            if (_pending.TryRemove(pending.Key, out var source))
            {
                source.TrySetException(exception);
            }
        }
    }

    private void SetHealth(
        EdgeConnectionState state,
        string? error = null,
        DateTimeOffset? lastMessageAt = null,
        DateTimeOffset? lastHeartbeatAt = null)
    {
        Health = new EdgeConnectionHealth(
            state,
            lastMessageAt ?? Health.LastMessageAt,
            lastHeartbeatAt ?? Health.LastHeartbeatAt,
            error);
        HealthChanged?.Invoke(this, Health);
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        _disposeCts.Cancel();
        SetHealth(EdgeConnectionState.Closed);

        if (_receiveTask is not null)
        {
            try
            {
                await _receiveTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        foreach (var watcher in _digitalWatchers.Values)
        {
            watcher.Writer.TryComplete();
        }

        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }

        _receiveCts?.Dispose();
        _connectionLock.Dispose();
        _disposeCts.Dispose();
    }
}

internal sealed class RemoteDigitalOutput : IDigitalOutput
{
    private readonly RemoteDevice _device;

    public RemoteDigitalOutput(RemoteDevice device, int channel)
    {
        _device = device;
        Channel = new DigitalChannel(channel);
    }

    public DigitalChannel Channel { get; }

    public ValueTask SetAsync(bool isHigh, CancellationToken cancellationToken = default)
    {
        return _device.SetDigitalOutputAsync(Channel.Number, isHigh, cancellationToken);
    }
}

internal sealed class RemoteDigitalInput : IDigitalInput
{
    private readonly RemoteDevice _device;
    private readonly DigitalInputOptions _options;

    public RemoteDigitalInput(RemoteDevice device, int channel, DigitalInputOptions options)
    {
        _device = device;
        _options = options;
        Channel = new DigitalChannel(channel);
    }

    public DigitalChannel Channel { get; }

    public ValueTask<DigitalInputState> ReadAsync(CancellationToken cancellationToken = default)
    {
        return _device.ReadDigitalInputAsync(Channel.Number, _options, cancellationToken);
    }

    public IAsyncEnumerable<DigitalInputState> WatchAsync(CancellationToken cancellationToken = default)
    {
        return _device.WatchDigitalInputAsync(Channel.Number, _options, cancellationToken);
    }
}

internal sealed class RemotePwmOutput : IPwmOutput
{
    private readonly RemoteDevice _device;

    public RemotePwmOutput(RemoteDevice device, int channel)
    {
        _device = device;
        Channel = new PwmChannel(channel);
    }

    public PwmChannel Channel { get; }

    public ValueTask SetDutyCycleAsync(double dutyCycle, CancellationToken cancellationToken = default)
    {
        return _device.SetPwmAsync(Channel.Number, dutyCycle, cancellationToken);
    }
}

internal sealed class RemoteMotor : IMotor
{
    private readonly RemoteDevice _device;

    public RemoteMotor(RemoteDevice device, string name)
    {
        _device = device;
        Channel = new MotorChannel(name);
    }

    public MotorChannel Channel { get; }

    public ValueTask SetSpeedAsync(double speed, CancellationToken cancellationToken = default)
    {
        return _device.SetMotorSpeedAsync(Channel.Name, speed, cancellationToken);
    }

    public ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        return SetSpeedAsync(0, cancellationToken);
    }
}

internal sealed class RemoteI2cDevice : II2cDevice
{
    private readonly RemoteDevice _device;

    public RemoteI2cDevice(RemoteDevice device, int bus, int address)
    {
        _device = device;
        Bus = new I2cBus(bus);
        Address = new I2cAddress(address);
    }

    public I2cBus Bus { get; }

    public I2cAddress Address { get; }

    public ValueTask<byte[]> ReadRegisterAsync(
        int register,
        int length,
        CancellationToken cancellationToken = default)
    {
        return _device.ReadI2cRegisterAsync(Bus.Number, Address.Value, register, length, cancellationToken);
    }

    public ValueTask WriteRegisterAsync(
        int register,
        IReadOnlyList<byte> data,
        CancellationToken cancellationToken = default)
    {
        return _device.WriteI2cRegisterAsync(Bus.Number, Address.Value, register, data, cancellationToken);
    }
}

internal sealed class RemoteCamera : ICamera
{
    private readonly RemoteDevice _device;

    public RemoteCamera(RemoteDevice device, string cameraId)
    {
        _device = device;
        CameraId = cameraId;
    }

    public string CameraId { get; }

    public ValueTask StartStreamAsync(CancellationToken cancellationToken = default)
    {
        return _device.StartCameraAsync(CameraId, cancellationToken);
    }

    public ValueTask StopStreamAsync(CancellationToken cancellationToken = default)
    {
        return _device.StopCameraAsync(CameraId, cancellationToken);
    }

    public ValueTask<CameraStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        return _device.GetCameraStatusAsync(CameraId, cancellationToken);
    }
}
