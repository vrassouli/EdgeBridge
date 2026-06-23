using System.Collections.Concurrent;
using System.Threading.Channels;
using EdgeBridge.Abstractions;
using EdgeBridge.Protocol;
using EdgeBridge.Transport.WebSockets;

namespace EdgeBridge.Client;

internal sealed class RemoteDevice : IDevice, IAsyncDisposable
{
    private readonly ITransportConnection _connection;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<CommandResponse>> _pending = new();
    private readonly ConcurrentDictionary<string, Channel<DigitalInputState>> _digitalWatchers = new();
    private readonly CancellationTokenSource _disposeCts = new();
    private Task? _receiveTask;

    public RemoteDevice(ITransportConnection connection)
    {
        _connection = connection;
    }

    public string DeviceId { get; private set; } = "remote";

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        _receiveTask = Task.Run(() => ReceiveLoopAsync(_disposeCts.Token), CancellationToken.None);
        var info = await GetInfoAsync(cancellationToken).ConfigureAwait(false);
        DeviceId = info.DeviceId;
    }

    public async ValueTask<DeviceInfo> GetInfoAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendCommandAsync(EdgeBridgeCommands.DeviceGetInfo, new { }, cancellationToken).ConfigureAwait(false);
        var payload = ProtocolJson.ReadPayload<DeviceInfoPayload>(response.Payload);
        return payload?.Device ?? throw new InvalidOperationException("Agent returned an empty device info payload.");
    }

    public IDigitalOutput DigitalOutput(int channel) => new RemoteDigitalOutput(this, channel);

    public IDigitalInput DigitalInput(int channel) => new RemoteDigitalInput(this, channel);

    public IPwmOutput PwmOutput(int channel) => new RemotePwmOutput(this, channel);

    public IMotor Motor(string name) => new RemoteMotor(this, name);

    internal ValueTask SetDigitalOutputAsync(int channel, bool isHigh, CancellationToken cancellationToken)
    {
        return SendCommandWithoutResultAsync(
            EdgeBridgeCommands.DigitalWrite,
            new DigitalWritePayload(channel, isHigh),
            cancellationToken);
    }

    internal async ValueTask<DigitalInputState> ReadDigitalInputAsync(int channel, CancellationToken cancellationToken)
    {
        var response = await SendCommandAsync(
            EdgeBridgeCommands.DigitalRead,
            new DigitalReadPayload(channel),
            cancellationToken).ConfigureAwait(false);
        var result = ProtocolJson.ReadPayload<DigitalReadResult>(response.Payload)
            ?? throw new InvalidOperationException("Agent returned an empty digital read payload.");

        return new DigitalInputState(new DigitalChannel(result.Channel), result.IsHigh, result.Timestamp);
    }

    internal async IAsyncEnumerable<DigitalInputState> WatchDigitalInputAsync(
        int channel,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var subscriptionId = $"digital:{channel}";
        var watcher = _digitalWatchers.GetOrAdd(subscriptionId, _ => Channel.CreateUnbounded<DigitalInputState>());

        var request = new SubscribeRequest
        {
            Type = MessageTypes.SubscribeRequest,
            DeviceId = DeviceId,
            Event = EdgeBridgeCommands.DigitalWatch,
            Payload = ProtocolJson.ToJsonElement(new DigitalWatchPayload(channel)),
            CorrelationId = subscriptionId
        };

        await _connection.SendAsync(request, cancellationToken).ConfigureAwait(false);

        await foreach (var state in watcher.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return state;
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

        await _connection.SendAsync(request, cancellationToken).ConfigureAwait(false);

        await using var registration = cancellationToken.Register(() => pending.TrySetCanceled(cancellationToken));
        var response = await pending.Task.ConfigureAwait(false);

        if (!response.Success)
        {
            throw new InvalidOperationException(response.Error?.Message ?? "EdgeBridge command failed.");
        }

        return response;
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var message in _connection.ReceiveAsync(cancellationToken).ConfigureAwait(false))
            {
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
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            foreach (var pending in _pending.Values)
            {
                pending.TrySetException(ex);
            }
        }
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

    public async ValueTask DisposeAsync()
    {
        _disposeCts.Cancel();

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

        await _connection.DisposeAsync().ConfigureAwait(false);
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

    public RemoteDigitalInput(RemoteDevice device, int channel)
    {
        _device = device;
        Channel = new DigitalChannel(channel);
    }

    public DigitalChannel Channel { get; }

    public ValueTask<DigitalInputState> ReadAsync(CancellationToken cancellationToken = default)
    {
        return _device.ReadDigitalInputAsync(Channel.Number, cancellationToken);
    }

    public IAsyncEnumerable<DigitalInputState> WatchAsync(CancellationToken cancellationToken = default)
    {
        return _device.WatchDigitalInputAsync(Channel.Number, cancellationToken);
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

