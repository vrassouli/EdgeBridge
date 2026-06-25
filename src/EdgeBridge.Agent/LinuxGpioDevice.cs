using System.Collections.Concurrent;
using System.Device.Gpio;
using System.Device.Gpio.Drivers;
using System.Device.Pwm;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using EdgeBridge.Abstractions;
using AbstractionsPwmChannel = EdgeBridge.Abstractions.PwmChannel;
using IoPwmChannel = System.Device.Pwm.PwmChannel;

namespace EdgeBridge.Agent;

internal sealed class LinuxGpioDevice : IDevice, IDisposable
{
    private readonly AgentConfig _config;
    private readonly GpioController _gpio;
    private readonly object _gpioLock = new();
    private readonly ConcurrentDictionary<int, IoPwmChannel> _pwmChannels = new();

    public LinuxGpioDevice(AgentConfig config)
    {
        _config = config;
        _gpio = CreateGpioController(config.Hardware.GpioChip);
        DeviceId = config.DeviceId;
    }

    public string DeviceId { get; }

    public ValueTask<DeviceInfo> GetInfoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var capabilities = new List<string> { "gpio.digital", "linux-gpio" };

        if (_config.Modules.Pwm)
        {
            capabilities.Add("pwm");
        }

        if (_config.Hardware.Motors.Count > 0)
        {
            capabilities.Add("motor");
        }

        if (_config.Modules.I2c)
        {
            capabilities.Add("i2c");
        }

        if (_config.Modules.Camera)
        {
            capabilities.Add("camera");
        }

        return ValueTask.FromResult(new DeviceInfo(
            DeviceId,
            _config.DeviceName,
            "linux-gpio-agent",
            capabilities));
    }

    public IDigitalOutput DigitalOutput(int channel) => new LinuxDigitalOutput(this, channel);

    public IDigitalInput DigitalInput(int channel) => DigitalInput(channel, DigitalInputOptions.Default);

    public IDigitalInput DigitalInput(int channel, DigitalInputOptions options) => new LinuxDigitalInput(this, channel, options);

    public IPwmOutput PwmOutput(int channel) => new LinuxPwmOutput(this, channel);

    public IMotor Motor(string name)
    {
        if (!_config.Hardware.Motors.TryGetValue(name, out var mapping))
        {
            return new UnsupportedMotor(name);
        }

        return new LinuxMappedMotor(this, name, mapping);
    }

    public II2cDevice I2cDevice(int bus, int address) => new UnsupportedI2cDevice(bus, address);

    public ICamera Camera(string cameraId) => new UnsupportedCamera(cameraId);

    public void Dispose()
    {
        foreach (var pwmChannel in _pwmChannels.Values)
        {
            pwmChannel.Dispose();
        }

        _gpio.Dispose();
    }

    internal ValueTask SetDigitalOutputAsync(int channel, bool isHigh, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gpioLock)
        {
            OpenPin(channel, PinMode.Output);
            _gpio.Write(channel, isHigh ? PinValue.High : PinValue.Low);
        }

        return ValueTask.CompletedTask;
    }

    internal ValueTask<DigitalInputState> ReadDigitalInputAsync(
        int channel,
        DigitalInputOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        PinValue value;
        lock (_gpioLock)
        {
            OpenPin(channel, ToPinMode(options.PullMode));
            value = _gpio.Read(channel);
        }

        return ValueTask.FromResult(ToDigitalInputState(channel, value));
    }

    internal async IAsyncEnumerable<DigitalInputState> WatchDigitalInputAsync(
        int channel,
        DigitalInputOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var events = Channel.CreateUnbounded<DigitalInputState>();

        PinChangeEventHandler handler = (_, args) =>
        {
            var isHigh = args.ChangeType == PinEventTypes.Rising;
            events.Writer.TryWrite(new DigitalInputState(
                new DigitalChannel(channel),
                isHigh,
                DateTimeOffset.UtcNow));
        };

        lock (_gpioLock)
        {
            OpenPin(channel, ToPinMode(options.PullMode));
            _gpio.RegisterCallbackForPinValueChangedEvent(
                channel,
                PinEventTypes.Rising | PinEventTypes.Falling,
                handler);
        }

        try
        {
            yield return await ReadDigitalInputAsync(channel, options, cancellationToken).ConfigureAwait(false);

            await foreach (var state in events.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return state;
            }
        }
        finally
        {
            lock (_gpioLock)
            {
                if (_gpio.IsPinOpen(channel))
                {
                    _gpio.UnregisterCallbackForPinValueChangedEvent(channel, handler);
                }
            }
        }
    }

    internal ValueTask SetPwmAsync(int channel, double dutyCycle, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_config.Modules.Pwm)
        {
            throw new NotSupportedException("PWM is disabled in agent configuration.");
        }

        var clampedDutyCycle = Math.Clamp(dutyCycle, 0, 1);
        var pwmChannel = _pwmChannels.GetOrAdd(channel, CreatePwmChannel);

        pwmChannel.DutyCycle = clampedDutyCycle;

        if (clampedDutyCycle == 0)
        {
            pwmChannel.Stop();
        }
        else
        {
            pwmChannel.Start();
        }

        return ValueTask.CompletedTask;
    }

    internal async ValueTask SetMotorSpeedAsync(
        string name,
        MotorMappingConfig mapping,
        double speed,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var clampedSpeed = Math.Clamp(speed, -1, 1);
        var dutyCycle = Math.Abs(clampedSpeed) * Math.Clamp(mapping.MaxDutyCycle, 0, 1);

        if (clampedSpeed < 0 && mapping.DirectionChannel is null)
        {
            throw new NotSupportedException(
                $"Motor '{name}' does not have a direction channel and cannot run in reverse.");
        }

        if (mapping.DirectionChannel is int directionChannel)
        {
            var directionHigh = clampedSpeed < 0;
            if (mapping.InvertDirection)
            {
                directionHigh = !directionHigh;
            }

            await SetDigitalOutputAsync(directionChannel, directionHigh, cancellationToken).ConfigureAwait(false);
        }

        await SetPwmAsync(mapping.PwmChannel, dutyCycle, cancellationToken).ConfigureAwait(false);
    }

    private IoPwmChannel CreatePwmChannel(int channel)
    {
        try
        {
            return IoPwmChannel.Create(
                _config.Hardware.PwmChip,
                channel,
                _config.Hardware.PwmFrequency,
                0);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"PWM channel {channel} could not be opened on pwmChip {_config.Hardware.PwmChip} at {_config.Hardware.PwmFrequency} Hz. " +
                "Verify that Linux sysfs PWM is enabled on the device, use 'ls /sys/class/pwm' to find the available pwmchip number, " +
                "then update hardware.pwmChip or disable the pwm module if sysfs PWM is not available.",
                ex);
        }
    }

    private static GpioController CreateGpioController(int gpioChip)
    {
        try
        {
            return new GpioController(new LibGpiodV2Driver(gpioChip));
        }
        catch (Exception ex) when (IsMissingLibGpiod(ex))
        {
            throw new InvalidOperationException(
                "The linux-gpio backend requires the native libgpiod V2 runtime. Install it on the device, then run the Agent again. On Raspberry Pi OS/Debian 13 trixie, use: sudo apt update && sudo apt install -y libgpiod-dev gpiod",
                ex);
        }
    }

    private static bool IsMissingLibGpiod(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("Libgpiod driver not installed", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("libgpiod", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void OpenPin(int channel, PinMode mode)
    {
        if (!_gpio.IsPinOpen(channel))
        {
            _gpio.OpenPin(channel, mode);
            return;
        }

        if (_gpio.GetPinMode(channel) != mode)
        {
            _gpio.SetPinMode(channel, mode);
        }
    }

    private static DigitalInputState ToDigitalInputState(int channel, PinValue value)
    {
        return new DigitalInputState(
            new DigitalChannel(channel),
            value == PinValue.High,
            DateTimeOffset.UtcNow);
    }

    private static PinMode ToPinMode(DigitalInputPullMode pullMode)
    {
        return pullMode switch
        {
            DigitalInputPullMode.PullDown => PinMode.InputPullDown,
            DigitalInputPullMode.PullUp => PinMode.InputPullUp,
            _ => PinMode.Input
        };
    }
}

internal sealed class LinuxDigitalOutput : IDigitalOutput
{
    private readonly LinuxGpioDevice _device;

    public LinuxDigitalOutput(LinuxGpioDevice device, int channel)
    {
        _device = device;
        Channel = new DigitalChannel(channel);
    }

    public DigitalChannel Channel { get; }

    public ValueTask SetAsync(bool isHigh, CancellationToken cancellationToken = default)
    {
        return _device.SetDigitalOutputAsync(Channel.Number, isHigh, cancellationToken);
    }
}

internal sealed class LinuxDigitalInput : IDigitalInput
{
    private readonly LinuxGpioDevice _device;
    private readonly DigitalInputOptions _options;

    public LinuxDigitalInput(LinuxGpioDevice device, int channel, DigitalInputOptions options)
    {
        _device = device;
        _options = options;
        Channel = new DigitalChannel(channel);
    }

    public DigitalChannel Channel { get; }

    public ValueTask<DigitalInputState> ReadAsync(CancellationToken cancellationToken = default)
    {
        return _device.ReadDigitalInputAsync(Channel.Number, _options, cancellationToken);
    }

    public IAsyncEnumerable<DigitalInputState> WatchAsync(CancellationToken cancellationToken = default)
    {
        return _device.WatchDigitalInputAsync(Channel.Number, _options, cancellationToken);
    }
}

internal sealed class LinuxPwmOutput : IPwmOutput
{
    private readonly LinuxGpioDevice _device;

    public LinuxPwmOutput(LinuxGpioDevice device, int channel)
    {
        _device = device;
        Channel = new AbstractionsPwmChannel(channel);
    }

    public AbstractionsPwmChannel Channel { get; }

    public ValueTask SetDutyCycleAsync(double dutyCycle, CancellationToken cancellationToken = default)
    {
        return _device.SetPwmAsync(Channel.Number, dutyCycle, cancellationToken);
    }
}

internal sealed class UnsupportedMotor : IMotor
{
    public UnsupportedMotor(string name)
    {
        Channel = new MotorChannel(name);
    }

    public MotorChannel Channel { get; }

    public ValueTask SetSpeedAsync(double speed, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "The linux-gpio backend does not map named motors yet. Use GPIO/PWM channels directly or add a motor mapping provider.");
    }

    public ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}

internal sealed class LinuxMappedMotor : IMotor
{
    private readonly LinuxGpioDevice _device;
    private readonly MotorMappingConfig _mapping;

    public LinuxMappedMotor(LinuxGpioDevice device, string name, MotorMappingConfig mapping)
    {
        _device = device;
        _mapping = mapping;
        Channel = new MotorChannel(name);
    }

    public MotorChannel Channel { get; }

    public ValueTask SetSpeedAsync(double speed, CancellationToken cancellationToken = default)
    {
        return _device.SetMotorSpeedAsync(Channel.Name, _mapping, speed, cancellationToken);
    }

    public ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        return SetSpeedAsync(0, cancellationToken);
    }
}

internal sealed class UnsupportedI2cDevice : II2cDevice
{
    public UnsupportedI2cDevice(int bus, int address)
    {
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
        throw new NotSupportedException(
            "The linux-gpio backend does not implement I2C register access yet.");
    }

    public ValueTask WriteRegisterAsync(
        int register,
        IReadOnlyList<byte> data,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "The linux-gpio backend does not implement I2C register access yet.");
    }
}

internal sealed class UnsupportedCamera : ICamera
{
    public UnsupportedCamera(string cameraId)
    {
        CameraId = cameraId;
    }

    public string CameraId { get; }

    public ValueTask StartStreamAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "The linux-gpio backend does not implement camera control yet.");
    }

    public ValueTask StopStreamAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "The linux-gpio backend does not implement camera control yet.");
    }

    public ValueTask<CameraStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "The linux-gpio backend does not implement camera control yet.");
    }
}
