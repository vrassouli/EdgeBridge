using EdgeBridge.Abstractions;

namespace EdgeBridge.Protocol;

public sealed record DigitalWritePayload(int Channel, bool IsHigh);

public sealed record DigitalReadPayload(int Channel);

public sealed record DigitalReadResult(int Channel, bool IsHigh, DateTimeOffset Timestamp);

public sealed record DigitalWatchPayload(int Channel);

public sealed record PwmSetPayload(int Channel, double DutyCycle);

public sealed record MotorSetSpeedPayload(string Name, double Speed);

public sealed record CameraStreamPayload(string CameraId);

public sealed record DeviceInfoPayload(DeviceInfo Device);

public sealed record PongPayload(string DeviceId, DateTimeOffset Timestamp);

