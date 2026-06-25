namespace EdgeBridge.Protocol;

public static class EdgeBridgeCommands
{
    public const string DigitalWrite = "gpio.digital.write";
    public const string DigitalRead = "gpio.digital.read";
    public const string DigitalWatch = "gpio.digital.watch";
    public const string PwmSet = "pwm.set";
    public const string MotorSetSpeed = "motor.setSpeed";
    public const string I2cReadRegister = "i2c.register.read";
    public const string I2cWriteRegister = "i2c.register.write";
    public const string CameraStartStream = "camera.startStream";
    public const string CameraStopStream = "camera.stopStream";
    public const string CameraGetStatus = "camera.getStatus";
    public const string DeviceGetInfo = "device.getInfo";
    public const string DeviceConfigGet = "device.config.get";
    public const string DeviceConfigUpdate = "device.config.update";
    public const string DevicePing = "device.ping";
}

public static class EdgeBridgeEvents
{
    public const string DigitalInputChanged = "gpio.digital.changed";
    public const string Heartbeat = "heartbeat";
}
