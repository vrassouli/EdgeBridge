using System.Text.Json;
using EdgeBridge.Abstractions;

namespace EdgeBridge.Protocol;

public abstract record ProtocolMessage
{
    public string ProtocolVersion { get; init; } = "1.0";

    public string MessageId { get; init; } = Guid.NewGuid().ToString("n");

    public string? DeviceId { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public required string Type { get; init; }

    public string? CorrelationId { get; init; }
}

public sealed record CommandRequest : ProtocolMessage
{
    public required string Command { get; init; }

    public JsonElement Payload { get; init; }
}

public sealed record CommandResponse : ProtocolMessage
{
    public bool Success { get; init; }

    public JsonElement Payload { get; init; }

    public EdgeBridgeError? Error { get; init; }
}

public sealed record SubscribeRequest : ProtocolMessage
{
    public required string Event { get; init; }

    public JsonElement Payload { get; init; }
}

public sealed record UnsubscribeRequest : ProtocolMessage
{
    public required string SubscriptionId { get; init; }
}

public sealed record EventMessage : ProtocolMessage
{
    public required string Event { get; init; }

    public string? SubscriptionId { get; init; }

    public JsonElement Payload { get; init; }
}

public sealed record ErrorMessage : ProtocolMessage
{
    public required EdgeBridgeError Error { get; init; }
}

public sealed record HeartbeatMessage : ProtocolMessage
{
    public string Runtime { get; init; } = "unknown";
}

public sealed record DeviceInfoMessage : ProtocolMessage
{
    public required DeviceInfo Device { get; init; }
}

public sealed record EdgeBridgeError(
    string Code,
    string Message,
    string? Detail = null);

