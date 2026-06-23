using EdgeBridge.Abstractions;

namespace EdgeBridge.Agent;

internal static class DeviceFactory
{
    public static IDevice Create(AgentConfig config)
    {
        return config.Hardware.Backend.Trim().ToLowerInvariant() switch
        {
            HardwareBackends.Mock => new MockDevice(config),
            HardwareBackends.LinuxGpio => CreateLinuxGpioDevice(config),
            _ => throw new InvalidOperationException(
                $"Hardware backend '{config.Hardware.Backend}' is not supported.")
        };
    }

    private static IDevice CreateLinuxGpioDevice(AgentConfig config)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException(
                "The linux-gpio hardware backend can only run on Linux.");
        }

        return new LinuxGpioDevice(config);
    }
}
