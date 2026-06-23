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
    private readonly AgentConfig _config;

    public MockDevice(AgentConfig config)
    {
        _config = config;
        DeviceId = config.DeviceId;
    }

    public string DeviceId { get; }

    public ValueTask<DeviceInfo> GetInfoAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> capabilities = ["gpio.digital", "pwm", "motor", "mock"];
        return ValueTask.FromResult(new DeviceInfo(DeviceId, _config.DeviceName, "mock-linux-agent", capabilities));
    }

    public IDigitalOutput DigitalOutput(int channel) => new MockDigitalOutput(this, channel);

    public IDigitalInput DigitalInput(int channel) => new MockDigitalInput(this, channel);

    public IPwmOutput PwmOutput(int channel) => new MockPwmOutput(this, channel);

    public IMotor Motor(string name) => new MockMotor(this, name);

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

    private void SetDigitalInput(int channel, bool isHigh)
    {
        _digitalInputs[channel] = isHigh;

        if (_watchers.TryGetValue(channel, out var watcher))
        {
            watcher.Writer.TryWrite(new DigitalInputState(new DigitalChannel(channel), isHigh, DateTimeOffset.UtcNow));
        }
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

