using System.Collections.Concurrent;
using System.Device.Gpio;
using System.Device.Gpio.Drivers;
using System.Device.I2c;
using System.Device.Pwm;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using EdgeBridge.Abstractions;
using AbstractionsI2cAddress = EdgeBridge.Abstractions.I2cAddress;
using AbstractionsI2cBus = EdgeBridge.Abstractions.I2cBus;
using AbstractionsPwmChannel = EdgeBridge.Abstractions.PwmChannel;
using IoI2cDevice = System.Device.I2c.I2cDevice;
using IoPwmChannel = System.Device.Pwm.PwmChannel;

namespace EdgeBridge.Agent;

internal sealed class LinuxGpioDevice : IDevice, IDisposable
{
    private readonly AgentConfig _config;
    private readonly GpioController _gpio;
    private readonly object _gpioLock = new();
    private readonly ConcurrentDictionary<int, IoPwmChannel> _pwmChannels = new();
    private readonly ConcurrentDictionary<I2cDeviceKey, IoI2cDevice> _i2cDevices = new();

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

    public II2cDevice I2cDevice(int bus, int address) => new LinuxI2cDevice(this, bus, address);

    public ICamera Camera(string cameraId) => new UnsupportedCamera(cameraId);

    public void Dispose()
    {
        foreach (var pwmChannel in _pwmChannels.Values)
        {
            pwmChannel.Dispose();
        }

        foreach (var i2cDevice in _i2cDevices.Values)
        {
            i2cDevice.Dispose();
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

    internal ValueTask<byte[]> ReadI2cRegisterAsync(
        int bus,
        int address,
        int register,
        int length,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureI2cEnabled();
        ValidateI2cRegister(register);

        if (length <= 0)
        {
            throw new InvalidOperationException("I2C read length must be greater than zero.");
        }

        var device = GetI2cDevice(bus, address);
        var registerBuffer = new[] { checked((byte)register) };
        var readBuffer = new byte[length];
        device.WriteRead(registerBuffer, readBuffer);

        return ValueTask.FromResult(readBuffer);
    }

    internal ValueTask WriteI2cRegisterAsync(
        int bus,
        int address,
        int register,
        IReadOnlyList<byte> data,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureI2cEnabled();
        ValidateI2cRegister(register);

        if (data.Count == 0)
        {
            throw new InvalidOperationException("I2C write payload must contain at least one byte.");
        }

        var device = GetI2cDevice(bus, address);
        var writeBuffer = new byte[data.Count + 1];
        writeBuffer[0] = checked((byte)register);

        for (var i = 0; i < data.Count; i++)
        {
            writeBuffer[i + 1] = data[i];
        }

        device.Write(writeBuffer);
        return ValueTask.CompletedTask;
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

    private IoI2cDevice GetI2cDevice(int bus, int address)
    {
        if (bus < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bus), bus, "I2C bus number must be non-negative.");
        }

        if (address is < 0 or > 0x7F)
        {
            throw new ArgumentOutOfRangeException(nameof(address), address, "I2C address must be a 7-bit address from 0x00 to 0x7F.");
        }

        return _i2cDevices.GetOrAdd(new I2cDeviceKey(bus, address), static key =>
        {
            try
            {
                return IoI2cDevice.Create(new I2cConnectionSettings(key.Bus, key.Address));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new InvalidOperationException(
                    $"I2C device 0x{key.Address:X2} could not be opened on bus {key.Bus}. " +
                    $"Verify that /dev/i2c-{key.Bus} exists, the Agent user can access it, and the device address is correct.",
                    ex);
            }
        });
    }

    private void EnsureI2cEnabled()
    {
        if (!_config.Modules.I2c)
        {
            throw new NotSupportedException("I2C is disabled in agent configuration.");
        }
    }

    private static void ValidateI2cRegister(int register)
    {
        if (register is < 0 or > 0xFF)
        {
            throw new ArgumentOutOfRangeException(nameof(register), register, "I2C register must be an 8-bit value from 0x00 to 0xFF.");
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

internal readonly record struct I2cDeviceKey(int Bus, int Address);

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

internal sealed class LinuxI2cDevice : II2cDevice
{
    private readonly LinuxGpioDevice _device;

    public LinuxI2cDevice(LinuxGpioDevice device, int bus, int address)
    {
        _device = device;
        Bus = new AbstractionsI2cBus(bus);
        Address = new AbstractionsI2cAddress(address);
    }

    public AbstractionsI2cBus Bus { get; }

    public AbstractionsI2cAddress Address { get; }

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
