namespace EdgeBridge.Abstractions;

public interface IDevice
{
    string DeviceId { get; }

    ValueTask<DeviceInfo> GetInfoAsync(CancellationToken cancellationToken = default);

    IDigitalOutput DigitalOutput(int channel);

    IDigitalInput DigitalInput(int channel);

    IDigitalInput DigitalInput(int channel, DigitalInputOptions options);

    IPwmOutput PwmOutput(int channel);

    IMotor Motor(string name);

    II2cDevice I2cDevice(int bus, int address);

    ICamera Camera(string cameraId);
}

public interface IDeviceModule
{
    string Name { get; }

    ValueTask InitializeAsync(CancellationToken cancellationToken = default);
}

public interface IDigitalOutput
{
    DigitalChannel Channel { get; }

    ValueTask SetAsync(bool isHigh, CancellationToken cancellationToken = default);
}

public interface IDigitalInput
{
    DigitalChannel Channel { get; }

    ValueTask<DigitalInputState> ReadAsync(CancellationToken cancellationToken = default);

    IAsyncEnumerable<DigitalInputState> WatchAsync(CancellationToken cancellationToken = default);
}

public interface IPwmOutput
{
    PwmChannel Channel { get; }

    ValueTask SetDutyCycleAsync(double dutyCycle, CancellationToken cancellationToken = default);
}

public interface IMotor
{
    MotorChannel Channel { get; }

    ValueTask SetSpeedAsync(double speed, CancellationToken cancellationToken = default);

    ValueTask StopAsync(CancellationToken cancellationToken = default);
}

public interface ICamera
{
    string CameraId { get; }

    ValueTask StartStreamAsync(CancellationToken cancellationToken = default);

    ValueTask StopStreamAsync(CancellationToken cancellationToken = default);

    ValueTask<CameraStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}

public interface II2cDevice
{
    I2cBus Bus { get; }

    I2cAddress Address { get; }

    ValueTask<byte[]> ReadRegisterAsync(
        int register,
        int length,
        CancellationToken cancellationToken = default);

    ValueTask WriteRegisterAsync(
        int register,
        IReadOnlyList<byte> data,
        CancellationToken cancellationToken = default);
}

public interface ISensor<T>
{
    string SensorId { get; }

    ValueTask<SensorReading<T>> ReadAsync(CancellationToken cancellationToken = default);

    IAsyncEnumerable<SensorReading<T>> WatchAsync(CancellationToken cancellationToken = default);
}
