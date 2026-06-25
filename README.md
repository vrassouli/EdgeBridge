# EdgeBridge

**EdgeBridge** is a hardware abstraction and remote device runtime platform for building applications that can talk to **local**, **remote**, or **simulated** hardware through the same application-facing API.

The core idea is simple:

> Application code should depend on `EdgeBridge.Abstractions`, not on Raspberry Pi, ESP32, Arduino, Jetson, GPIO libraries, transport details, or simulator internals.

That lets the same business/application code run against:

- a local hardware implementation,
- a remote device through an EdgeBridge Agent,
- or a simulator/mock device during development and testing.

The default implementation uses mock hardware so it can run on macOS immediately. The Agent can also use a `linux-gpio` hardware backend on Linux devices, including Raspberry Pi, without changing application-facing abstractions.

---

## Why EdgeBridge?

When building robotics, IoT, home automation, educational hardware projects, or edge-device experiments, application code often becomes tightly coupled to a specific board, GPIO package, protocol, or deployment environment.

EdgeBridge tries to avoid that by separating:

- **what the application wants to do** — read inputs, set outputs, drive motors, read sensors,
- from **where/how the hardware exists** — local board, remote agent, simulator, or future device family.

This makes it easier to:

- develop from a Mac or PC while the hardware is somewhere else,
- run samples with mock hardware before wiring real hardware,
- use real GPIO on Linux/Raspberry Pi when available,
- swap transports without changing public APIs,
- add ESP32, Arduino, Jetson, camera, and sensor providers later,
- teach robotics or hardware programming with less setup friction.

---

## Current Status

EdgeBridge is in early MVP stage.

Implemented now:

- shared async hardware abstractions,
- versioned JSON protocol messages,
- WebSocket transport,
- Linux/Raspberry Pi-ready Agent skeleton,
- mock hardware provider,
- Linux GPIO hardware backend,
- configuration-driven Linux GPIO/PWM motor mapping,
- remote I2C register access contracts with mock and Linux support,
- camera control/status contracts with mock support,
- durable Agent config get/update protocol with restart-required semantics,
- remote client SDK,
- client connection health reporting and explicit reconnect support,
- provisioning contracts,
- professional all-in-one Avalonia GUI sample for device management,
- Linux install notes and systemd packaging,
- lightweight test harness covering protocol serialization, command correlation, profile persistence, mock behavior, I2C/camera controls, config updates, watch unsubscribe behavior, and client health/reconnect behavior.

Not implemented yet:

- authentication/authorization for WebSocket transport,
- real WiFi/Bluetooth provisioning flows,
- camera stream transport,
- real Linux camera hardware provider,
- full client-agent integration coverage against a real WebSocket listener.

See [`TODO.md`](TODO.md) for the active roadmap.

---

## Repository Layout

```text
.
├── EdgeBridge.slnx
├── README.md
├── TODO.md
├── AGENTS.md
├── config/
│   └── agent.example.json
├── docs/
│   ├── linux-agent.md
│   └── adr/
│       ├── 0001-mvp-architecture.md
│       └── 0002-linux-gpio-hardware-backend.md
├── packaging/
│   └── systemd/
│       └── edgebridge-agent.service
└── src/
    ├── EdgeBridge.Abstractions/
    ├── EdgeBridge.Protocol/
    ├── EdgeBridge.Transport.WebSockets/
    ├── EdgeBridge.Agent/
    ├── EdgeBridge.Client/
    ├── EdgeBridge.Provisioning/
    ├── EdgeBridge.Samples.Avalonia/
    └── EdgeBridge.Samples.Avalonia.Android/
```

### Projects

| Project | Purpose |
| --- | --- |
| `EdgeBridge.Abstractions` | Public application-facing hardware interfaces and shared device models. |
| `EdgeBridge.Protocol` | Versioned JSON protocol messages, command payloads, errors, and serialization options. |
| `EdgeBridge.Transport.WebSockets` | Replaceable transport contracts plus the current WebSocket implementation. |
| `EdgeBridge.Agent` | Device-side runtime/daemon. Hosts the selected hardware backend, currently `mock` or `linux-gpio`. |
| `EdgeBridge.Client` | Remote proxy implementation that exposes `IDevice` over a transport connection. |
| `EdgeBridge.Provisioning` | Contracts for future device setup/provisioning flows such as WiFi/Bluetooth onboarding. |
| `EdgeBridge.Samples.Avalonia` | Professional all-in-one Avalonia GUI sample for managing devices and feature pages. |
| `EdgeBridge.Samples.Avalonia.Android` | Android host project for the shared Avalonia GUI. Not included in the default solution build because Android workloads are optional. |

---

## Requirements

- .NET 10 SDK
- macOS, Linux, or Raspberry Pi OS

For mock hardware, no native GPIO dependency is required.

For the `linux-gpio` backend on Raspberry Pi OS/Debian 13 trixie-based images, install:

```bash
sudo apt update
sudo apt install -y libgpiod-dev gpiod
```

---

## Quick Start

### 1. Clone and build

```bash
git clone https://github.com/vrassouli/EdgeBridge.git
cd EdgeBridge
dotnet build
```

### 2. Start the Agent

```bash
dotnet run --project src/EdgeBridge.Agent
```

By default, the Agent uses a mock device and listens on:

```text
http://localhost:8080/edgebridge/
```

Remote clients connect using the WebSocket form:

```text
ws://localhost:8080/edgebridge/
```

### 3. Run the all-in-one GUI sample

The Avalonia sample is the primary sample surface. It persists local device profiles, connects to a selected Agent, and exposes dynamic pages for GPIO, PWM, motors, I2C register access, camera control/status, and Agent configuration.

Agent-side command failures must be handled by the GUI as user-visible status messages. A failed remote command, such as an unavailable module, should not crash the app.

```bash
dotnet run --project src/EdgeBridge.Samples.Avalonia
```

The default endpoint is:

```text
ws://localhost:8080/edgebridge/
```

Profiles are stored in the current user's app data folder under `EdgeBridge/sample-devices.json`.

### Android host

The Android host project is present under `src/EdgeBridge.Samples.Avalonia.Android`, but it is intentionally not part of `EdgeBridge.slnx` so normal builds do not require Android workloads.

To build or run it, install the Android workload and SDK first:

```bash
dotnet workload install android
dotnet build src/EdgeBridge.Samples.Avalonia.Android
```

---

## Agent Configuration

The Agent can load a JSON config file:

```json
{
  "deviceId": "toy-car-01",
  "deviceName": "Toy Car",
  "hardware": {
    "backend": "mock",
    "gpioChip": 0,
    "pwmChip": 0,
    "pwmFrequency": 1000,
    "motors": {
      "left": {
        "pwmChannel": 0,
        "directionChannel": 23,
        "invertDirection": false,
        "maxDutyCycle": 1.0
      }
    },
    "i2cDevices": [
      {
        "name": "Sensor",
        "bus": 1,
        "address": 64
      }
    ],
    "cameras": [
      {
        "cameraId": "camera0",
        "name": "Camera 0",
        "enabled": true
      }
    ]
  },
  "transports": {
    "webSocket": {
      "enabled": true,
      "url": "http://localhost:8080/edgebridge/"
    }
  },
  "modules": {
    "gpio": true,
    "pwm": true,
    "i2c": true,
    "camera": true
  }
}
```

Run with an explicit config file:

```bash
dotnet run --project src/EdgeBridge.Agent -- --config=agent.json
```

When no `--config` argument is provided, the Agent first tries:

```text
/etc/edgebridge/agent.json
```

If that file does not exist, it uses built-in development defaults. Config updates sent by the GUI are persisted to the active config path when one exists, otherwise to a user-writable app data config path. Updated config returns `restartRequired: true`; restart the Agent before expecting backend, chip, module, I2C, camera, or motor mapping changes to affect the running device.

For a device on your network, add a GUI profile using the device IP or hostname, for example `ws://DEVICE_IP:8080/edgebridge/`.

### Real GPIO backend

Set the hardware backend to `linux-gpio` on a Linux device to use real GPIO lines behind `IDevice`:

```json
{
  "hardware": {
    "backend": "linux-gpio",
    "gpioChip": 0,
    "pwmChip": 0,
    "pwmFrequency": 1000
  }
}
```

Channel numbers are Linux GPIO line offsets on the selected GPIO chip, not Raspberry Pi board-specific pin names. Use `gpioinfo` on the target device to inspect available GPIO chips and line offsets.

Digital inputs default to plain floating inputs. If an input channel is disconnected, the physical pin may rapidly switch between High and Low due to electrical noise. Give watched inputs a defined idle state with a pull-up or pull-down resistor, or connect them through hardware that actively drives both states. The client API and GUI profile can request `Floating`, `PullDown`, or `PullUp`; the `linux-gpio` backend maps those options to the platform input modes, including Raspberry Pi internal pull resistors when supported by the OS driver.

Named motors can be mapped in Agent configuration without changing application code. Each motor maps to a PWM channel and can optionally use a direction GPIO channel:

```json
{
  "hardware": {
    "backend": "linux-gpio",
    "gpioChip": 0,
    "pwmChip": 0,
    "pwmFrequency": 1000,
    "motors": {
      "left": {
        "pwmChannel": 0,
        "directionChannel": 23,
        "invertDirection": false,
        "maxDutyCycle": 1.0
      },
      "right": {
        "pwmChannel": 1,
        "directionChannel": 24,
        "invertDirection": true,
        "maxDutyCycle": 0.85
      }
    }
  }
}
```

Negative motor speeds require a configured `directionChannel`. If no direction channel is configured, the motor can only run forward or stop.

When `modules.i2c` is enabled, the `linux-gpio` backend uses Linux I2C device files such as `/dev/i2c-1` for 8-bit register reads and writes. The Agent user must be allowed to access the selected I2C bus, often by membership in the `i2c` group on Raspberry Pi OS:

```bash
sudo usermod -aG i2c edgebridge
sudo systemctl restart edgebridge-agent
```

Use `i2cdetect -l` to list available buses. The current register API uses a single 8-bit register address followed by the read or write payload.

---

## Using the Client SDK

Application code talks to `IDevice`.

```csharp
using EdgeBridge.Abstractions;
using EdgeBridge.Client;

IDevice device = await EdgeDevice.ConnectAsync("ws://localhost:8080/edgebridge/");

var led = device.DigitalOutput(17);
await led.SetAsync(true);
await Task.Delay(500);
await led.SetAsync(false);
```

The important part is that application code uses `EdgeBridge.Abstractions`. The implementation can be local hardware, a remote Agent, or a simulator.

---

## Core Abstractions

The MVP abstraction layer currently includes:

- `IDevice`
- `IDigitalOutput`
- `IDigitalInput`
- `IPwmOutput`
- `IMotor`
- `II2cDevice`
- `ICamera`
- `ISensor<T>`

All public APIs are async-first and accept `CancellationToken`.

---

## Protocol Overview

The current transport uses versioned JSON protocol messages over WebSockets.

Message types include:

- command request/response,
- subscribe/unsubscribe request,
- event messages,
- error messages,
- heartbeat messages,
- device info messages.

Protocol messages include a protocol version, message ID, optional device ID, timestamp, type, and optional correlation ID.

---

## Linux / Raspberry Pi Agent

The Agent can be published as a self-contained Linux executable.

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

See [`docs/linux-agent.md`](docs/linux-agent.md) for copy, install, direct development runs, real GPIO setup, and `systemd` instructions.

---

## Architecture Principles

EdgeBridge follows these rules:

- Application code depends only on `EdgeBridge.Abstractions`.
- Abstractions never depend on implementations.
- Client SDK and Agent expose equivalent behavior.
- Public APIs are async-first.
- Public APIs accept `CancellationToken`.
- Transports are replaceable.
- Protocol messages are versionable.
- Names avoid board-specific terminology.
- The design should support Raspberry Pi, ESP32, Arduino, Jetson, simulators, and future devices.

Accepted architecture decisions:

- [`ADR 0001: MVP Architecture`](docs/adr/0001-mvp-architecture.md)
- [`ADR 0002: Linux GPIO Hardware Backend`](docs/adr/0002-linux-gpio-hardware-backend.md)
- [`ADR 0003: Primary GUI Sample and Agent Config Updates`](docs/adr/0003-primary-gui-sample-and-agent-config-updates.md)

---

## Development Notes

Build everything:

```bash
dotnet build
```

Run the Agent:

```bash
dotnet run --project src/EdgeBridge.Agent
```

Run the GUI sample:

```bash
dotnet run --project src/EdgeBridge.Samples.Avalonia
```

---

## Roadmap

Near-term work:

- add WebSocket authentication and authorization,
- implement WiFi/Bluetooth provisioning,
- add camera stream protocol and transport support,
- implement a real Linux camera hardware provider,
- expand client-agent integration tests against a real WebSocket listener,
- add more ADRs for protocol versioning, transport replacement, and hardware provider model.

---

## Documentation for Agents

This repository is intended to be self-documenting for human developers and coding agents.

Important files:

- [`AGENTS.md`](AGENTS.md) — coding-agent rules and project memory instructions.
- [`TODO.md`](TODO.md) — current roadmap.
- [`docs/adr/`](docs/adr/) — architecture decision records.
- [`docs/linux-agent.md`](docs/linux-agent.md) — Linux/Raspberry Pi deployment notes.

When a new long-term rule, architecture decision, naming decision, protocol requirement, or product direction is accepted, it should be recorded in the appropriate documentation instead of living only in chat history.

---

## License

No license file is currently present. Add a license before publishing packages or encouraging external reuse.
