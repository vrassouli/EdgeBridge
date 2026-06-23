using System.Net;
using EdgeBridge.Abstractions;
using EdgeBridge.Protocol;
using EdgeBridge.Transport.WebSockets;

namespace EdgeBridge.Agent;

internal sealed class AgentWebSocketServer
{
    private readonly HttpListener _listener = new();
    private readonly IDevice _device;
    private readonly Uri _listenUri;

    public AgentWebSocketServer(IDevice device, Uri listenUri)
    {
        _device = device;
        _listenUri = listenUri;
        _listener.Prefixes.Add(ToHttpListenerPrefix(listenUri));
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _listener.Start();
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
        var heartbeatTask = SendHeartbeatsAsync(connection, heartbeatCts.Token);

        try
        {
            await foreach (var message in connection.ReceiveAsync(cancellationToken).ConfigureAwait(false))
            {
                await HandleMessageAsync(connection, message, cancellationToken).ConfigureAwait(false);
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
        CancellationToken cancellationToken)
    {
        switch (message)
        {
            case CommandRequest command:
                await HandleCommandAsync(connection, command, cancellationToken).ConfigureAwait(false);
                break;
            case SubscribeRequest subscription:
                await HandleSubscriptionAsync(connection, subscription, cancellationToken).ConfigureAwait(false);
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
                EdgeBridgeCommands.DevicePing => ProtocolJson.ToJsonElement(new PongPayload(
                    _device.DeviceId,
                    DateTimeOffset.UtcNow)),
                EdgeBridgeCommands.DigitalWrite => await WriteDigitalOutputAsync(request, cancellationToken).ConfigureAwait(false),
                EdgeBridgeCommands.DigitalRead => await ReadDigitalInputAsync(request, cancellationToken).ConfigureAwait(false),
                EdgeBridgeCommands.PwmSet => await SetPwmAsync(request, cancellationToken).ConfigureAwait(false),
                EdgeBridgeCommands.MotorSetSpeed => await SetMotorAsync(request, cancellationToken).ConfigureAwait(false),
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
            await connection.SendAsync(new CommandResponse
            {
                Type = MessageTypes.CommandResponse,
                DeviceId = _device.DeviceId,
                CorrelationId = request.MessageId,
                Success = false,
                Error = new EdgeBridgeError("command_failed", ex.Message)
            }, cancellationToken).ConfigureAwait(false);
        }
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

        var state = await _device.DigitalInput(payload.Channel).ReadAsync(cancellationToken).ConfigureAwait(false);
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

    private async Task HandleSubscriptionAsync(
        ITransportConnection connection,
        SubscribeRequest request,
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
        var subscriptionId = request.CorrelationId ?? $"digital:{payload.Channel}";

        _ = Task.Run(async () =>
        {
            await foreach (var state in _device.DigitalInput(payload.Channel).WatchAsync(cancellationToken).ConfigureAwait(false))
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
                }, cancellationToken).ConfigureAwait(false);
            }
        }, CancellationToken.None);
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
