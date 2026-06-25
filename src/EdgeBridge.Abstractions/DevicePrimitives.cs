namespace EdgeBridge.Abstractions;

public readonly record struct DigitalChannel(int Number);

public enum DigitalInputPullMode
{
    Floating,
    PullDown,
    PullUp
}

public readonly record struct DigitalInputOptions(DigitalInputPullMode PullMode)
{
    public static DigitalInputOptions Default { get; } = new(DigitalInputPullMode.Floating);
}

public readonly record struct PwmChannel(int Number);

public readonly record struct MotorChannel(string Name);

public readonly record struct I2cBus(int Number);

public readonly record struct I2cAddress(int Value);

public readonly record struct DigitalInputState(
    DigitalChannel Channel,
    bool IsHigh,
    DateTimeOffset Timestamp);

public sealed record CameraStatus(
    string CameraId,
    bool IsStreaming,
    DateTimeOffset Timestamp);

public readonly record struct SensorReading<T>(
    string SensorId,
    T Value,
    DateTimeOffset Timestamp);

public sealed record DeviceInfo(
    string DeviceId,
    string DeviceName,
    string Runtime,
    IReadOnlyList<string> Capabilities);
