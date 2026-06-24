namespace EdgeBridge.Client;

public enum EdgeConnectionState
{
    Connecting,
    Connected,
    Reconnecting,
    Closed,
    Faulted
}

public sealed record EdgeConnectionHealth(
    EdgeConnectionState State,
    DateTimeOffset? LastMessageAt = null,
    DateTimeOffset? LastHeartbeatAt = null,
    string? LastError = null);

public interface IRemoteDeviceConnection
{
    EdgeConnectionHealth Health { get; }

    event EventHandler<EdgeConnectionHealth>? HealthChanged;

    ValueTask ReconnectAsync(CancellationToken cancellationToken = default);
}
