namespace EdgeBridge.Agent;

internal sealed record AgentConfig
{
    public string DeviceId { get; init; } = "edgebridge-dev";

    public string DeviceName { get; init; } = "EdgeBridge Development Device";

    public TransportConfig Transports { get; init; } = new();

    public ModuleConfig Modules { get; init; } = new();
}

internal sealed record TransportConfig
{
    public WebSocketConfig WebSocket { get; init; } = new();
}

internal sealed record WebSocketConfig
{
    public bool Enabled { get; init; } = true;

    public string Url { get; init; } = "http://localhost:8080/edgebridge/";
}

internal sealed record ModuleConfig
{
    public bool Gpio { get; init; } = true;

    public bool Pwm { get; init; } = true;

    public bool Camera { get; init; } = false;
}

