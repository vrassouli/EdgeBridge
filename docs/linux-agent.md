# Linux/Raspberry Pi Agent

The Agent can run with mock hardware on any Linux device supported by .NET 10, or with the `linux-gpio` backend for real GPIO on devices that expose Linux GPIO chips.

## Publish from Mac

For Raspberry Pi 64-bit:

```bash
dotnet publish src/EdgeBridge.Agent -c Release -r linux-arm64 --self-contained true -o publish/agent-linux-arm64
```

For Raspberry Pi 32-bit:

```bash
dotnet publish src/EdgeBridge.Agent -c Release -r linux-arm --self-contained true -o publish/agent-linux-arm
```

For generic x64 Linux:

```bash
dotnet publish src/EdgeBridge.Agent -c Release -r linux-x64 --self-contained true -o publish/agent-linux-x64
```

## Install on Linux

Copy the published directory to the device:

```bash
scp -r publish/agent-linux-arm64 pi@DEVICE_IP:/tmp/edgebridge-agent
scp config/agent.example.json pi@DEVICE_IP:/tmp/agent.json
scp packaging/systemd/edgebridge-agent.service pi@DEVICE_IP:/tmp/edgebridge-agent.service
```

On the device:

```bash
sudo apt update
sudo apt install -y libgpiod-dev gpiod
sudo useradd --system --home /opt/edgebridge --shell /usr/sbin/nologin edgebridge || true
sudo mkdir -p /opt/edgebridge/agent /etc/edgebridge
sudo cp -r /tmp/edgebridge-agent/* /opt/edgebridge/agent/
sudo cp /tmp/agent.json /etc/edgebridge/agent.json
sudo chown -R edgebridge:edgebridge /opt/edgebridge
sudo cp /tmp/edgebridge-agent.service /etc/systemd/system/edgebridge-agent.service
sudo systemctl daemon-reload
sudo systemctl enable --now edgebridge-agent
```

Check status:

```bash
systemctl status edgebridge-agent
journalctl -u edgebridge-agent -f
```

When no `--config` argument is provided on Linux, the Agent first tries `/etc/edgebridge/agent.json`.
If that file does not exist, it uses built-in development defaults.

From your MacBook, run a sample against the device:

```bash
dotnet run --project src/EdgeBridge.Samples.Console -- ws://DEVICE_IP:8080/edgebridge/ blink
```

## Development Raspberry Pi

The current development Raspberry Pi 4 is reachable at `rpi4-dev.local` with SSH user `me`.

For development checks, do not install or run the Agent as a Raspberry Pi service. Publish the Agent from the Mac, copy the published files into `~/edgebridge-agent` on the Pi, place `agent.json` beside the binary, and run it directly from that directory:

```bash
dotnet publish src/EdgeBridge.Agent -c Release -r linux-arm64 --self-contained true -o publish/agent-linux-arm64
rsync -az --delete publish/agent-linux-arm64/ me@rpi4-dev.local:~/edgebridge-agent/
scp config/agent.example.json me@rpi4-dev.local:~/edgebridge-agent/agent.json
ssh me@rpi4-dev.local 'cd ~/edgebridge-agent && ./EdgeBridge.Agent --config=agent.json'
```

Then verify client connectivity from the Mac:

```bash
dotnet run --project src/EdgeBridge.Samples.Console -- ws://rpi4-dev.local:8080/edgebridge/ blink
```

## Real GPIO backend

To use physical GPIO lines, set the hardware backend in `/etc/edgebridge/agent.json`:

```json
{
  "deviceId": "lab-device-01",
  "deviceName": "Lab Device",
  "hardware": {
    "backend": "linux-gpio",
    "gpioChip": 0,
    "pwmChip": 0,
    "pwmFrequency": 1000
  },
  "transports": {
    "webSocket": {
      "enabled": true,
      "url": "http://0.0.0.0:8080/edgebridge/"
    }
  },
  "modules": {
    "gpio": true,
    "pwm": true,
    "camera": false
  }
}
```

The `linux-gpio` backend uses the libgpiod V2 ABI. On Raspberry Pi OS or Debian 13/trixie-based images, install the libgpiod development package and command-line inspection tool:

```bash
sudo apt update
sudo apt install -y libgpiod-dev gpiod
```

Channel numbers are Linux GPIO line offsets on the configured GPIO chip. On many Raspberry Pi OS images, `gpioChip` 0 maps to `/dev/gpiochip0`; verify the line numbers for your board with:

```bash
gpioinfo
```

The service account must be allowed to access the GPIO chip devices. On Raspberry Pi OS this usually means adding it to the `gpio` group:

```bash
sudo usermod -aG gpio edgebridge
sudo systemctl restart edgebridge-agent
```

The Avalonia RGB LED sample toggles LED channels through `IDigitalOutput`, so it does not require Linux PWM support.

If PWM is enabled, the backend uses Linux sysfs PWM through `pwmChip` and `pwmFrequency`. Raspberry Pi OS does not always expose `/sys/class/pwm/pwmchip0` by default. On Raspberry Pi OS Bookworm, hardware PWM is configured through `/boot/firmware/config.txt`; older images may use `/boot/config.txt`. For example, `dtoverlay=pwm-2chan` exposes hardware PWM on GPIO 18 and 19 after a reboot. Use `ls /sys/class/pwm` to find the enabled PWM chip number, then set `hardware.pwmChip` to match. Disable `"pwm"` in `modules` if your board image does not expose PWM through sysfs yet.
