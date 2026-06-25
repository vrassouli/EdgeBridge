using EdgeBridge.Protocol;

namespace EdgeBridge.Agent;

internal sealed record AgentConfig
{
    public string DeviceId { get; init; } = "edgebridge-dev";

    public string DeviceName { get; init; } = "EdgeBridge Development Device";

    public HardwareConfig Hardware { get; init; } = new();

    public TransportConfig Transports { get; init; } = new();

    public ModuleConfig Modules { get; init; } = new();
}

internal sealed record HardwareConfig
{
    public string Backend { get; init; } = HardwareBackends.Mock;

    public int GpioChip { get; init; } = 0;

    public int PwmChip { get; init; } = 0;

    public int PwmFrequency { get; init; } = 1000;

    public Dictionary<string, MotorMappingConfig> Motors { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public List<I2cDeviceConfig> I2cDevices { get; init; } = [];

    public List<CameraConfig> Cameras { get; init; } = [];
}

internal sealed record MotorMappingConfig
{
    public int PwmChannel { get; init; }

    public int? DirectionChannel { get; init; }

    public bool InvertDirection { get; init; }

    public double MaxDutyCycle { get; init; } = 1;
}

internal sealed record I2cDeviceConfig
{
    public string Name { get; init; } = "I2C Device";

    public int Bus { get; init; } = 1;

    public int Address { get; init; }
}

internal sealed record CameraConfig
{
    public string CameraId { get; init; } = "camera0";

    public string Name { get; init; } = "Camera";

    public bool Enabled { get; init; }
}

internal static class HardwareBackends
{
    public const string Mock = "mock";

    public const string LinuxGpio = "linux-gpio";
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

    public bool I2c { get; init; } = false;

    public bool Camera { get; init; } = false;
}

internal static class AgentConfigMapper
{
    public static AgentConfigDto ToDto(this AgentConfig config)
    {
        return new AgentConfigDto
        {
            DeviceId = config.DeviceId,
            DeviceName = config.DeviceName,
            Hardware = new AgentHardwareConfigDto
            {
                Backend = config.Hardware.Backend,
                GpioChip = config.Hardware.GpioChip,
                PwmChip = config.Hardware.PwmChip,
                PwmFrequency = config.Hardware.PwmFrequency,
                Motors = config.Hardware.Motors.ToDictionary(
                    pair => pair.Key,
                    pair => new AgentMotorMappingConfigDto
                    {
                        PwmChannel = pair.Value.PwmChannel,
                        DirectionChannel = pair.Value.DirectionChannel,
                        InvertDirection = pair.Value.InvertDirection,
                        MaxDutyCycle = pair.Value.MaxDutyCycle
                    },
                    StringComparer.OrdinalIgnoreCase),
                I2cDevices = config.Hardware.I2cDevices.Select(device => new AgentI2cDeviceConfigDto
                {
                    Name = device.Name,
                    Bus = device.Bus,
                    Address = device.Address
                }).ToList(),
                Cameras = config.Hardware.Cameras.Select(camera => new AgentCameraConfigDto
                {
                    CameraId = camera.CameraId,
                    Name = camera.Name,
                    Enabled = camera.Enabled
                }).ToList()
            },
            Transports = new AgentTransportConfigDto
            {
                WebSocket = new AgentWebSocketConfigDto
                {
                    Enabled = config.Transports.WebSocket.Enabled,
                    Url = config.Transports.WebSocket.Url
                }
            },
            Modules = new AgentModuleConfigDto
            {
                Gpio = config.Modules.Gpio,
                Pwm = config.Modules.Pwm,
                I2c = config.Modules.I2c,
                Camera = config.Modules.Camera
            }
        };
    }

    public static AgentConfig ToAgentConfig(this AgentConfigDto config)
    {
        return new AgentConfig
        {
            DeviceId = config.DeviceId,
            DeviceName = config.DeviceName,
            Hardware = new HardwareConfig
            {
                Backend = config.Hardware.Backend,
                GpioChip = config.Hardware.GpioChip,
                PwmChip = config.Hardware.PwmChip,
                PwmFrequency = config.Hardware.PwmFrequency,
                Motors = config.Hardware.Motors.ToDictionary(
                    pair => pair.Key,
                    pair => new MotorMappingConfig
                    {
                        PwmChannel = pair.Value.PwmChannel,
                        DirectionChannel = pair.Value.DirectionChannel,
                        InvertDirection = pair.Value.InvertDirection,
                        MaxDutyCycle = pair.Value.MaxDutyCycle
                    },
                    StringComparer.OrdinalIgnoreCase),
                I2cDevices = config.Hardware.I2cDevices.Select(device => new I2cDeviceConfig
                {
                    Name = device.Name,
                    Bus = device.Bus,
                    Address = device.Address
                }).ToList(),
                Cameras = config.Hardware.Cameras.Select(camera => new CameraConfig
                {
                    CameraId = camera.CameraId,
                    Name = camera.Name,
                    Enabled = camera.Enabled
                }).ToList()
            },
            Transports = new TransportConfig
            {
                WebSocket = new WebSocketConfig
                {
                    Enabled = config.Transports.WebSocket.Enabled,
                    Url = config.Transports.WebSocket.Url
                }
            },
            Modules = new ModuleConfig
            {
                Gpio = config.Modules.Gpio,
                Pwm = config.Modules.Pwm,
                I2c = config.Modules.I2c,
                Camera = config.Modules.Camera
            }
        };
    }
}
