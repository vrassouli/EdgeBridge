# Linux/Raspberry Pi Agent

The MVP Agent has no GPIO dependency yet, so it can run on any Linux device supported by .NET 10. The same steps work for Raspberry Pi OS using the matching runtime identifier.

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

From your MacBook, run a sample against the device:

```bash
dotnet run --project src/EdgeBridge.Samples.Console -- ws://DEVICE_IP:8080/edgebridge/ blink
```
