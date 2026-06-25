using EdgeBridge.Abstractions;

namespace EdgeBridge.Protocol;

public sealed record DigitalWritePayload(int Channel, bool IsHigh);

public sealed record DigitalReadPayload(int Channel, DigitalInputPullMode PullMode = DigitalInputPullMode.Floating);

public sealed record DigitalReadResult(int Channel, bool IsHigh, DateTimeOffset Timestamp);

public sealed record DigitalWatchPayload(int Channel, DigitalInputPullMode PullMode = DigitalInputPullMode.Floating);

public sealed record PwmSetPayload(int Channel, double DutyCycle);

public sealed record MotorSetSpeedPayload(string Name, double Speed);

public sealed record I2cReadRegisterPayload(int Bus, int Address, int Register, int Length);

public sealed record I2cReadRegisterResult(int Bus, int Address, int Register, byte[] Data, DateTimeOffset Timestamp);

public sealed record I2cWriteRegisterPayload(int Bus, int Address, int Register, byte[] Data);

public sealed record CameraStreamPayload(string CameraId);

public sealed record CameraStatusPayload(CameraStatus Status);

public sealed record DeviceInfoPayload(DeviceInfo Device);

public sealed record AgentConfigPayload(AgentConfigDto Config);

public sealed record AgentConfigUpdateResult(bool Accepted, bool RestartRequired, string Message, AgentConfigDto Config);

public sealed record PongPayload(string DeviceId, DateTimeOffset Timestamp);
