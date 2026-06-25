namespace EdgeBridge.Protocol;

public sealed record AgentConfigDto
{
    public string DeviceId { get; init; } = "edgebridge-dev";

    public string DeviceName { get; init; } = "EdgeBridge Development Device";

    public AgentHardwareConfigDto Hardware { get; init; } = new();

    public AgentTransportConfigDto Transports { get; init; } = new();

    public AgentModuleConfigDto Modules { get; init; } = new();
}

public sealed record AgentHardwareConfigDto
{
    public string Backend { get; init; } = "mock";

    public int GpioChip { get; init; }

    public int PwmChip { get; init; }

    public int PwmFrequency { get; init; } = 1000;

    public Dictionary<string, AgentMotorMappingConfigDto> Motors { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    public List<AgentI2cDeviceConfigDto> I2cDevices { get; init; } = [];

    public List<AgentCameraConfigDto> Cameras { get; init; } = [];
}

public sealed record AgentMotorMappingConfigDto
{
    public int PwmChannel { get; init; }

    public int? DirectionChannel { get; init; }

    public bool InvertDirection { get; init; }

    public double MaxDutyCycle { get; init; } = 1;
}

public sealed record AgentI2cDeviceConfigDto
{
    public string Name { get; init; } = "I2C Device";

    public int Bus { get; init; } = 1;

    public int Address { get; init; }
}

public sealed record AgentCameraConfigDto
{
    public string CameraId { get; init; } = "camera0";

    public string Name { get; init; } = "Camera";

    public bool Enabled { get; init; }
}

public sealed record AgentTransportConfigDto
{
    public AgentWebSocketConfigDto WebSocket { get; init; } = new();
}

public sealed record AgentWebSocketConfigDto
{
    public bool Enabled { get; init; } = true;

    public string Url { get; init; } = "http://localhost:8080/edgebridge/";
}

public sealed record AgentModuleConfigDto
{
    public bool Gpio { get; init; } = true;

    public bool Pwm { get; init; } = true;

    public bool I2c { get; init; }

    public bool Camera { get; init; }
}
