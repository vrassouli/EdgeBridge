using System.Text;
using System.Text.Json;
using EdgeBridge.Protocol;

namespace EdgeBridge.Transport.WebSockets;

public static class WebSocketMessageSerializer
{
    public static ArraySegment<byte> Serialize(ProtocolMessage message)
    {
        var json = message switch
        {
            CommandRequest value => JsonSerializer.Serialize(value, ProtocolJson.Options),
            CommandResponse value => JsonSerializer.Serialize(value, ProtocolJson.Options),
            SubscribeRequest value => JsonSerializer.Serialize(value, ProtocolJson.Options),
            UnsubscribeRequest value => JsonSerializer.Serialize(value, ProtocolJson.Options),
            EventMessage value => JsonSerializer.Serialize(value, ProtocolJson.Options),
            ErrorMessage value => JsonSerializer.Serialize(value, ProtocolJson.Options),
            HeartbeatMessage value => JsonSerializer.Serialize(value, ProtocolJson.Options),
            DeviceInfoMessage value => JsonSerializer.Serialize(value, ProtocolJson.Options),
            _ => throw new NotSupportedException($"Unsupported message type {message.GetType().Name}.")
        };

        return new ArraySegment<byte>(Encoding.UTF8.GetBytes(json));
    }

    public static ProtocolMessage Deserialize(ReadOnlySpan<byte> payload)
    {
        using var document = JsonDocument.Parse(payload.ToArray());
        var root = document.RootElement;
        var type = root.GetProperty("type").GetString();

        return type switch
        {
            MessageTypes.CommandRequest => JsonSerializer.Deserialize<CommandRequest>(root.GetRawText(), ProtocolJson.Options)!,
            MessageTypes.CommandResponse => JsonSerializer.Deserialize<CommandResponse>(root.GetRawText(), ProtocolJson.Options)!,
            MessageTypes.SubscribeRequest => JsonSerializer.Deserialize<SubscribeRequest>(root.GetRawText(), ProtocolJson.Options)!,
            MessageTypes.UnsubscribeRequest => JsonSerializer.Deserialize<UnsubscribeRequest>(root.GetRawText(), ProtocolJson.Options)!,
            MessageTypes.Event => JsonSerializer.Deserialize<EventMessage>(root.GetRawText(), ProtocolJson.Options)!,
            MessageTypes.Error => JsonSerializer.Deserialize<ErrorMessage>(root.GetRawText(), ProtocolJson.Options)!,
            MessageTypes.Heartbeat => JsonSerializer.Deserialize<HeartbeatMessage>(root.GetRawText(), ProtocolJson.Options)!,
            MessageTypes.DeviceInfo => JsonSerializer.Deserialize<DeviceInfoMessage>(root.GetRawText(), ProtocolJson.Options)!,
            _ => throw new JsonException($"Unknown EdgeBridge message type '{type}'.")
        };
    }
}

public static class MessageTypes
{
    public const string CommandRequest = "command.request";
    public const string CommandResponse = "command.response";
    public const string SubscribeRequest = "subscribe.request";
    public const string UnsubscribeRequest = "unsubscribe.request";
    public const string Event = "event";
    public const string Error = "error";
    public const string Heartbeat = "heartbeat";
    public const string DeviceInfo = "device.info";
}
