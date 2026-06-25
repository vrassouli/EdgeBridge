using System.Collections.Concurrent;
using System.Threading.Channels;
using EdgeBridge.Abstractions;

namespace EdgeBridge.Agent;

internal sealed class MockDevice : IDevice
{
    private readonly ConcurrentDictionary<int, bool> _digitalOutputs = new();
    private readonly ConcurrentDictionary<int, bool> _digitalInputs = new();
    private readonly ConcurrentDictionary<int, Channel<DigitalInputState>> _watchers = new();
    private readonly ConcurrentDictionary<int, double> _pwmOutputs = new();
    private readonly ConcurrentDictionary<string, double> _motors = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte[]> _i2cRegisters = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> _cameraStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly AgentConfig _config;

    public MockDevice(AgentConfig config)
    {
        _config = config;
        DeviceId = config.DeviceId;
    }

    public string DeviceId { get; }

    public ValueTask<DeviceInfo> GetInfoAsync(CancellationToken cancellationToken = default)
    {
        var capabilities = new List<string> { "gpio.digital", "pwm", "motor", "mock" };
        if (_config.Modules.I2c)
        {
            capabilities.Add("i2c");
        }

        if (_config.Modules.Camera)
        {
            capabilities.Add("camera");
        }

        return ValueTask.FromResult(new DeviceInfo(DeviceId, _config.DeviceName, "mock-linux-agent", capabilities));
    }

    public IDigitalOutput DigitalOutput(int channel) => new MockDigitalOutput(this, channel);

    public IDigitalInput DigitalInput(int channel) => DigitalInput(channel, DigitalInputOptions.Default);

    public IDigitalInput DigitalInput(int channel, DigitalInputOptions options) => new MockDigitalInput(this, channel);

    public IPwmOutput PwmOutput(int channel) => new MockPwmOutput(this, channel);

    public IMotor Motor(string name) => new MockMotor(this, name);

    public II2cDevice I2cDevice(int bus, int address) => new MockI2cDevice(this, bus, address);

    public ICamera Camera(string cameraId) => new MockCamera(this, cameraId);

    internal ValueTask SetDigitalOutputAsync(int channel, bool isHigh)
    {
        _digitalOutputs[channel] = isHigh;
        SetDigitalInput(channel, isHigh);
        return ValueTask.CompletedTask;
    }

    internal ValueTask<DigitalInputState> ReadDigitalInputAsync(int channel)
    {
        var value = _digitalInputs.GetOrAdd(channel, _ => false);
        return ValueTask.FromResult(new DigitalInputState(new DigitalChannel(channel), value, DateTimeOffset.UtcNow));
    }

    internal IAsyncEnumerable<DigitalInputState> WatchDigitalInputAsync(int channel, CancellationToken cancellationToken)
    {
        var watcher = _watchers.GetOrAdd(channel, _ => Channel.CreateUnbounded<DigitalInputState>());
        return watcher.Reader.ReadAllAsync(cancellationToken);
    }

    internal ValueTask SetPwmAsync(int channel, double dutyCycle)
    {
        _pwmOutputs[channel] = Math.Clamp(dutyCycle, 0, 1);
        return ValueTask.CompletedTask;
    }

    internal ValueTask SetMotorSpeedAsync(string name, double speed)
    {
        _motors[name] = Math.Clamp(speed, -1, 1);
        return ValueTask.CompletedTask;
    }

    internal ValueTask<byte[]> ReadI2cRegisterAsync(int bus, int address, int register, int length, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_config.Modules.I2c)
        {
            throw new NotSupportedException("I2C is disabled in agent configuration.");
        }

        if (length <= 0)
        {
            throw new InvalidOperationException("I2C read length must be greater than zero.");
        }

        var value = _i2cRegisters.GetOrAdd(I2cKey(bus, address, register), _ => new byte[length]);
        var result = new byte[length];
        Array.Copy(value, result, Math.Min(value.Length, result.Length));
        return ValueTask.FromResult(result);
    }

    internal ValueTask WriteI2cRegisterAsync(
        int bus,
        int address,
        int register,
        IReadOnlyList<byte> data,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_config.Modules.I2c)
        {
            throw new NotSupportedException("I2C is disabled in agent configuration.");
        }

        _i2cRegisters[I2cKey(bus, address, register)] = data.ToArray();
        return ValueTask.CompletedTask;
    }

    internal ValueTask SetCameraStreamingAsync(string cameraId, bool isStreaming, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_config.Modules.Camera)
        {
            throw new NotSupportedException("Camera is disabled in agent configuration.");
        }

        _cameraStates[cameraId] = isStreaming;
        return ValueTask.CompletedTask;
    }

    internal ValueTask<CameraStatus> GetCameraStatusAsync(string cameraId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_config.Modules.Camera)
        {
            throw new NotSupportedException("Camera is disabled in agent configuration.");
        }

        return ValueTask.FromResult(new CameraStatus(
            cameraId,
            _cameraStates.GetOrAdd(cameraId, _ => false),
            DateTimeOffset.UtcNow));
    }

    private void SetDigitalInput(int channel, bool isHigh)
    {
        _digitalInputs[channel] = isHigh;

        if (_watchers.TryGetValue(channel, out var watcher))
        {
            watcher.Writer.TryWrite(new DigitalInputState(new DigitalChannel(channel), isHigh, DateTimeOffset.UtcNow));
        }
    }

    private static string I2cKey(int bus, int address, int register)
    {
        return $"{bus}:{address:x2}:{register:x4}";
    }
}

internal sealed class MockDigitalOutput : IDigitalOutput
{
    private readonly MockDevice _device;

    public MockDigitalOutput(MockDevice device, int channel)
    {
        _device = device;
        Channel = new DigitalChannel(channel);
    }

    public DigitalChannel Channel { get; }

    public ValueTask SetAsync(bool isHigh, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _device.SetDigitalOutputAsync(Channel.Number, isHigh);
    }
}

internal sealed class MockDigitalInput : IDigitalInput
{
    private readonly MockDevice _device;

    public MockDigitalInput(MockDevice device, int channel)
    {
        _device = device;
        Channel = new DigitalChannel(channel);
    }

    public DigitalChannel Channel { get; }

    public ValueTask<DigitalInputState> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _device.ReadDigitalInputAsync(Channel.Number);
    }

    public IAsyncEnumerable<DigitalInputState> WatchAsync(CancellationToken cancellationToken = default)
    {
        return _device.WatchDigitalInputAsync(Channel.Number, cancellationToken);
    }
}

internal sealed class MockPwmOutput : IPwmOutput
{
    private readonly MockDevice _device;

    public MockPwmOutput(MockDevice device, int channel)
    {
        _device = device;
        Channel = new PwmChannel(channel);
    }

    public PwmChannel Channel { get; }

    public ValueTask SetDutyCycleAsync(double dutyCycle, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _device.SetPwmAsync(Channel.Number, dutyCycle);
    }
}

internal sealed class MockMotor : IMotor
{
    private readonly MockDevice _device;

    public MockMotor(MockDevice device, string name)
    {
        _device = device;
        Channel = new MotorChannel(name);
    }

    public MotorChannel Channel { get; }

    public ValueTask SetSpeedAsync(double speed, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _device.SetMotorSpeedAsync(Channel.Name, speed);
    }

    public ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        return SetSpeedAsync(0, cancellationToken);
    }
}

internal sealed class MockI2cDevice : II2cDevice
{
    private readonly MockDevice _device;

    public MockI2cDevice(MockDevice device, int bus, int address)
    {
        _device = device;
        Bus = new I2cBus(bus);
        Address = new I2cAddress(address);
    }

    public I2cBus Bus { get; }

    public I2cAddress Address { get; }

    public ValueTask<byte[]> ReadRegisterAsync(
        int register,
        int length,
        CancellationToken cancellationToken = default)
    {
        return _device.ReadI2cRegisterAsync(Bus.Number, Address.Value, register, length, cancellationToken);
    }

    public ValueTask WriteRegisterAsync(
        int register,
        IReadOnlyList<byte> data,
        CancellationToken cancellationToken = default)
    {
        return _device.WriteI2cRegisterAsync(Bus.Number, Address.Value, register, data, cancellationToken);
    }
}

internal sealed class MockCamera : ICamera
{
    private readonly MockDevice _device;

    public MockCamera(MockDevice device, string cameraId)
    {
        _device = device;
        CameraId = cameraId;
    }

    public string CameraId { get; }

    public ValueTask StartStreamAsync(CancellationToken cancellationToken = default)
    {
        return _device.SetCameraStreamingAsync(CameraId, true, cancellationToken);
    }

    public ValueTask StopStreamAsync(CancellationToken cancellationToken = default)
    {
        return _device.SetCameraStreamingAsync(CameraId, false, cancellationToken);
    }

    public ValueTask<CameraStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        return _device.GetCameraStatusAsync(CameraId, cancellationToken);
    }
}
