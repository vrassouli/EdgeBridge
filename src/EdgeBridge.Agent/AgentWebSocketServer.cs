using System.Net;
using System.Collections.Concurrent;
using EdgeBridge.Abstractions;
using EdgeBridge.Protocol;
using EdgeBridge.Transport.WebSockets;

namespace EdgeBridge.Agent;

internal sealed class AgentWebSocketServer
{
    private readonly HttpListener _listener = new();
    private readonly IDevice _device;
    private readonly AgentConfigStore _configStore;
    private readonly Uri _listenUri;

    public AgentWebSocketServer(IDevice device, AgentConfigStore configStore, Uri listenUri)
    {
        _device = device;
        _configStore = configStore;
        _listenUri = listenUri;
        _listener.Prefixes.Add(ToHttpListenerPrefix(listenUri));
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var httpPrefix = ToHttpListenerPrefix(_listenUri);

        try
        {
            _listener.Start();
        }
        catch (HttpListenerException ex)
        {
            throw new InvalidOperationException(
                $"Could not start EdgeBridge Agent listener on {httpPrefix}. " +
                "Another Agent or service may already be using this endpoint.",
                ex);
        }

        Console.WriteLine($"EdgeBridge Agent HTTP prefix: {httpPrefix}");
        Console.WriteLine($"EdgeBridge Agent listening on {ToWebSocketUri(_listenUri)}");

        using var registration = cancellationToken.Register(() => _listener.Stop());

        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;

            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            if (!context.Request.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.BadRequest;
                context.Response.Close();
                continue;
            }

            _ = Task.Run(() => HandleClientAsync(context, cancellationToken), CancellationToken.None);
        }
    }

    private async Task HandleClientAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var webSocketContext = await context.AcceptWebSocketAsync(subProtocol: null).ConfigureAwait(false);
        await using var connection = new WebSocketTransportConnection(webSocketContext.WebSocket);

        Console.WriteLine("Client connected.");

        using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var subscriptions = new ConcurrentDictionary<string, CancellationTokenSource>();
        var heartbeatTask = SendHeartbeatsAsync(connection, heartbeatCts.Token);

        try
        {
            await foreach (var message in connection.ReceiveAsync(cancellationToken).ConfigureAwait(false))
            {
                await HandleMessageAsync(connection, message, subscriptions, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Client connection error: {ex.Message}");
        }
        finally
        {
            heartbeatCts.Cancel();
            foreach (var subscription in subscriptions.Values)
            {
                subscription.Cancel();
                subscription.Dispose();
            }

            try
            {
                await heartbeatTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            Console.WriteLine("Client disconnected.");
        }
    }

    private async Task HandleMessageAsync(
        ITransportConnection connection,
        ProtocolMessage message,
        ConcurrentDictionary<string, CancellationTokenSource> subscriptions,
        CancellationToken cancellationToken)
    {
        switch (message)
        {
            case CommandRequest command:
                await HandleCommandAsync(connection, command, cancellationToken).ConfigureAwait(false);
                break;
            case SubscribeRequest subscription:
                await HandleSubscriptionAsync(connection, subscription, subscriptions, cancellationToken).ConfigureAwait(false);
                break;
            case UnsubscribeRequest unsubscribe:
                HandleUnsubscribe(unsubscribe, subscriptions);
                break;
        }
    }

    private async Task HandleCommandAsync(
        ITransportConnection connection,
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = request.Command switch
            {
                EdgeBridgeCommands.DeviceGetInfo => ProtocolJson.ToJsonElement(new DeviceInfoPayload(
                    await _device.GetInfoAsync(cancellationToken).ConfigureAwait(false))),
                EdgeBridgeCommands.DeviceConfigGet => ProtocolJson.ToJsonElement(new AgentConfigPayload(
                    _configStore.Current.ToDto())),
                EdgeBridgeCommands.DeviceConfigUpdate => await UpdateConfigAsync(request, cancellationToken).ConfigureAwait(false),
                EdgeBridgeCommands.DevicePing => ProtocolJson.ToJsonElement(new PongPayload(
                    _device.DeviceId,
                    DateTimeOffset.UtcNow)),
                EdgeBridgeCommands.DigitalWrite => await WriteDigitalOutputAsync(request, cancellationToken).ConfigureAwait(false),
                EdgeBridgeCommands.DigitalRead => await ReadDigitalInputAsync(request, cancellationToken).ConfigureAwait(false),
                EdgeBridgeCommands.PwmSet => await SetPwmAsync(request, cancellationToken).ConfigureAwait(false),
                EdgeBridgeCommands.MotorSetSpeed => await SetMotorAsync(request, cancellationToken).ConfigureAwait(false),
                EdgeBridgeCommands.I2cReadRegister => await ReadI2cRegisterAsync(request, cancellationToken).ConfigureAwait(false),
                EdgeBridgeCommands.I2cWriteRegister => await WriteI2cRegisterAsync(request, cancellationToken).ConfigureAwait(false),
                EdgeBridgeCommands.CameraStartStream => await StartCameraAsync(request, cancellationToken).ConfigureAwait(false),
                EdgeBridgeCommands.CameraStopStream => await StopCameraAsync(request, cancellationToken).ConfigureAwait(false),
                EdgeBridgeCommands.CameraGetStatus => await GetCameraStatusAsync(request, cancellationToken).ConfigureAwait(false),
                _ => throw new NotSupportedException($"Command '{request.Command}' is not supported by this agent.")
            };

            await connection.SendAsync(new CommandResponse
            {
                Type = MessageTypes.CommandResponse,
                DeviceId = _device.DeviceId,
                CorrelationId = request.MessageId,
                Success = true,
                Payload = payload
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Command '{request.Command}' failed: {ex.GetType().Name}: {ex.Message}");

            await connection.SendAsync(new CommandResponse
            {
                Type = MessageTypes.CommandResponse,
                DeviceId = _device.DeviceId,
                CorrelationId = request.MessageId,
                Success = false,
                Payload = ProtocolJson.ToJsonElement(new { }),
                Error = new EdgeBridgeError("command_failed", ex.Message)
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask<System.Text.Json.JsonElement> UpdateConfigAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        var payload = ProtocolJson.ReadPayload<AgentConfigPayload>(request.Payload)
            ?? throw new InvalidOperationException("Agent config payload is required.");

        var result = await _configStore.UpdateAsync(payload.Config, cancellationToken).ConfigureAwait(false);
        return ProtocolJson.ToJsonElement(result);
    }

    private async ValueTask<System.Text.Json.JsonElement> WriteDigitalOutputAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        var payload = ProtocolJson.ReadPayload<DigitalWritePayload>(request.Payload)
            ?? throw new InvalidOperationException("Digital write payload is required.");

        await _device.DigitalOutput(payload.Channel).SetAsync(payload.IsHigh, cancellationToken).ConfigureAwait(false);
        return ProtocolJson.ToJsonElement(new { ok = true });
    }

    private async ValueTask<System.Text.Json.JsonElement> ReadDigitalInputAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        var payload = ProtocolJson.ReadPayload<DigitalReadPayload>(request.Payload)
            ?? throw new InvalidOperationException("Digital read payload is required.");

        var state = await _device
            .DigitalInput(payload.Channel, new DigitalInputOptions(payload.PullMode))
            .ReadAsync(cancellationToken)
            .ConfigureAwait(false);
        return ProtocolJson.ToJsonElement(new DigitalReadResult(state.Channel.Number, state.IsHigh, state.Timestamp));
    }

    private async ValueTask<System.Text.Json.JsonElement> SetPwmAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        var payload = ProtocolJson.ReadPayload<PwmSetPayload>(request.Payload)
            ?? throw new InvalidOperationException("PWM payload is required.");

        await _device.PwmOutput(payload.Channel).SetDutyCycleAsync(payload.DutyCycle, cancellationToken).ConfigureAwait(false);
        return ProtocolJson.ToJsonElement(new { ok = true });
    }

    private async ValueTask<System.Text.Json.JsonElement> SetMotorAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        var payload = ProtocolJson.ReadPayload<MotorSetSpeedPayload>(request.Payload)
            ?? throw new InvalidOperationException("Motor payload is required.");

        await _device.Motor(payload.Name).SetSpeedAsync(payload.Speed, cancellationToken).ConfigureAwait(false);
        return ProtocolJson.ToJsonElement(new { ok = true });
    }

    private async ValueTask<System.Text.Json.JsonElement> ReadI2cRegisterAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        var payload = ProtocolJson.ReadPayload<I2cReadRegisterPayload>(request.Payload)
            ?? throw new InvalidOperationException("I2C read payload is required.");

        var data = await _device.I2cDevice(payload.Bus, payload.Address)
            .ReadRegisterAsync(payload.Register, payload.Length, cancellationToken)
            .ConfigureAwait(false);

        return ProtocolJson.ToJsonElement(new I2cReadRegisterResult(
            payload.Bus,
            payload.Address,
            payload.Register,
            data,
            DateTimeOffset.UtcNow));
    }

    private async ValueTask<System.Text.Json.JsonElement> WriteI2cRegisterAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        var payload = ProtocolJson.ReadPayload<I2cWriteRegisterPayload>(request.Payload)
            ?? throw new InvalidOperationException("I2C write payload is required.");

        await _device.I2cDevice(payload.Bus, payload.Address)
            .WriteRegisterAsync(payload.Register, payload.Data, cancellationToken)
            .ConfigureAwait(false);

        return ProtocolJson.ToJsonElement(new { ok = true });
    }

    private async ValueTask<System.Text.Json.JsonElement> StartCameraAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        var payload = ProtocolJson.ReadPayload<CameraStreamPayload>(request.Payload)
            ?? throw new InvalidOperationException("Camera payload is required.");

        await _device.Camera(payload.CameraId).StartStreamAsync(cancellationToken).ConfigureAwait(false);
        return ProtocolJson.ToJsonElement(new { ok = true });
    }

    private async ValueTask<System.Text.Json.JsonElement> StopCameraAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        var payload = ProtocolJson.ReadPayload<CameraStreamPayload>(request.Payload)
            ?? throw new InvalidOperationException("Camera payload is required.");

        await _device.Camera(payload.CameraId).StopStreamAsync(cancellationToken).ConfigureAwait(false);
        return ProtocolJson.ToJsonElement(new { ok = true });
    }

    private async ValueTask<System.Text.Json.JsonElement> GetCameraStatusAsync(
        CommandRequest request,
        CancellationToken cancellationToken)
    {
        var payload = ProtocolJson.ReadPayload<CameraStreamPayload>(request.Payload)
            ?? throw new InvalidOperationException("Camera payload is required.");

        var status = await _device.Camera(payload.CameraId).GetStatusAsync(cancellationToken).ConfigureAwait(false);
        return ProtocolJson.ToJsonElement(new CameraStatusPayload(status));
    }

    private async Task HandleSubscriptionAsync(
        ITransportConnection connection,
        SubscribeRequest request,
        ConcurrentDictionary<string, CancellationTokenSource> subscriptions,
        CancellationToken cancellationToken)
    {
        if (request.Event != EdgeBridgeCommands.DigitalWatch)
        {
            await connection.SendAsync(new ErrorMessage
            {
                Type = MessageTypes.Error,
                DeviceId = _device.DeviceId,
                CorrelationId = request.MessageId,
                Error = new EdgeBridgeError("subscription_not_supported", $"Event '{request.Event}' is not supported.")
            }, cancellationToken).ConfigureAwait(false);
            return;
        }

        var payload = ProtocolJson.ReadPayload<DigitalWatchPayload>(request.Payload)
            ?? throw new InvalidOperationException("Digital watch payload is required.");
        var subscriptionId = request.CorrelationId ?? request.MessageId;
        var subscriptionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        if (subscriptions.TryRemove(subscriptionId, out var existingSubscription))
        {
            existingSubscription.Cancel();
            existingSubscription.Dispose();
        }

        subscriptions[subscriptionId] = subscriptionCts;

        _ = Task.Run(async () =>
        {
            try
            {
                await foreach (var state in _device
                    .DigitalInput(payload.Channel, new DigitalInputOptions(payload.PullMode))
                    .WatchAsync(subscriptionCts.Token)
                    .ConfigureAwait(false))
                {
                    await connection.SendAsync(new EventMessage
                    {
                        Type = MessageTypes.Event,
                        DeviceId = _device.DeviceId,
                        Event = EdgeBridgeEvents.DigitalInputChanged,
                        SubscriptionId = subscriptionId,
                        Payload = ProtocolJson.ToJsonElement(new DigitalReadResult(
                            state.Channel.Number,
                            state.IsHigh,
                            state.Timestamp))
                    }, subscriptionCts.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                if (subscriptions.TryRemove(subscriptionId, out var completedSubscription))
                {
                    completedSubscription.Dispose();
                }
            }
        }, CancellationToken.None);
    }

    private static void HandleUnsubscribe(
        UnsubscribeRequest request,
        ConcurrentDictionary<string, CancellationTokenSource> subscriptions)
    {
        if (subscriptions.TryRemove(request.SubscriptionId, out var subscription))
        {
            subscription.Cancel();
            subscription.Dispose();
        }
    }

    private async Task SendHeartbeatsAsync(ITransportConnection connection, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));

        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            await connection.SendAsync(new HeartbeatMessage
            {
                Type = MessageTypes.Heartbeat,
                DeviceId = _device.DeviceId,
                Runtime = "agent"
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string ToHttpListenerPrefix(Uri uri)
    {
        var scheme = uri.Scheme is "ws" or "wss"
            ? uri.Scheme == "wss" ? "https" : "http"
            : uri.Scheme;
        var host = uri.Host is "0.0.0.0" or "::" ? "+" : uri.Host;
        var path = uri.AbsolutePath.EndsWith('/') ? uri.AbsolutePath : uri.AbsolutePath + "/";

        return $"{scheme}://{host}:{uri.Port}{path}";
    }

    private static Uri ToWebSocketUri(Uri uri)
    {
        if (uri.Scheme is "ws" or "wss")
        {
            return uri;
        }

        var builder = new UriBuilder(uri)
        {
            Scheme = uri.Scheme == "https" ? "wss" : "ws"
        };
        return builder.Uri;
    }

    private static class StatusCodes
    {
        public const int BadRequest = 400;
    }
}
