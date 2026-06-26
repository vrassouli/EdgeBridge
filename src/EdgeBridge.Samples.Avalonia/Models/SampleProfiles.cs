using System.Collections.ObjectModel;
using EdgeBridge.Abstractions;

namespace EdgeBridge.Samples.Avalonia.Models;

public sealed record DeviceProfileStore
{
    public ObservableCollection<DeviceProfile> Devices { get; init; } = [];
}

public sealed record DeviceProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; } = "Local Mock Device";

    public string Endpoint { get; set; } = "ws://localhost:8080/edgebridge/";

    public ObservableCollection<GpioChannelProfile> GpioChannels { get; init; } =
    [
        new() { Name = "LED 17", Channel = 17, Direction = GpioDirection.Output },
        new() { Name = "Button 17", Channel = 17, Direction = GpioDirection.Input }
    ];

    public ObservableCollection<PwmChannelProfile> PwmChannels { get; init; } =
    [
        new() { Name = "PWM 0", Channel = 0, DutyCycle = 0.25 }
    ];

    public ObservableCollection<MotorProfile> Motors { get; init; } =
    [
        new() { Name = "left" },
        new() { Name = "right" }
    ];

    public ObservableCollection<I2cDeviceProfile> I2cDevices { get; init; } =
    [
        new() { Name = "Sensor Read", Operation = I2cOperation.Read, Bus = 1, Address = 0x40, Register = 0, ReadLength = 2 },
        new() { Name = "Sensor Write", Operation = I2cOperation.Write, Bus = 1, Address = 0x40, Register = 0, WriteBytes = "00" }
    ];

    public ObservableCollection<CameraProfile> Cameras { get; init; } =
    [
        new() { CameraId = "camera0", Name = "Camera 0" }
    ];
}

public enum GpioDirection
{
    Input,
    Output
}

public sealed record GpioChannelProfile
{
    public string Name { get; set; } = "GPIO";

    public int Channel { get; set; }

    public GpioDirection Direction { get; set; }

    public DigitalInputPullMode PullMode { get; set; }

    public bool LastValue { get; set; }
}

public sealed record PwmChannelProfile
{
    public string Name { get; set; } = "PWM";

    public int Channel { get; set; }

    public double DutyCycle { get; set; }
}

public sealed record MotorProfile
{
    public string Name { get; set; } = "motor";

    public double Speed { get; set; }
}

public sealed record I2cDeviceProfile
{
    public string Name { get; set; } = "I2C Device";

    public I2cOperation Operation { get; set; }

    public I2cValueFormat ValueFormat { get; set; }

    public int Bus { get; set; } = 1;

    public int Address { get; set; }

    public int Register { get; set; }

    public int ReadLength { get; set; } = 1;

    public string WriteBytes { get; set; } = "00";

    public string LastRead { get; set; } = "";
}

public enum I2cOperation
{
    Read,
    Write
}

public enum I2cValueFormat
{
    Hex,
    Decimal
}

public sealed record CameraProfile
{
    public string CameraId { get; set; } = "camera0";

    public string Name { get; set; } = "Camera";

    public bool IsStreaming { get; set; }
}
