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
- remote client SDK,
- client connection health reporting and explicit reconnect support,
- provisioning contracts,
- console samples,
- cross-platform Avalonia GUI sample for RGB LED toggle control,
- Linux install notes and systemd packaging,
- lightweight test harness covering protocol serialization, command correlation, mock behavior, watch unsubscribe behavior, and client health/reconnect behavior.

Not implemented yet:

- authentication/authorization for WebSocket transport,
- real WiFi/Bluetooth provisioning flows,
- camera stream transport,
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
    ├── EdgeBridge.Samples.Console/
    └── EdgeBridge.Samples.Avalonia/
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
| `EdgeBridge.Samples.Console` | Console samples for blinking, button watching, and toy-car control. |
| `EdgeBridge.Samples.Avalonia` | Cross-platform desktop GUI sample for toggling RGB LED GPIO channels. |

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

### 3. Run a sample from another terminal

```bash
dotnet run --project src/EdgeBridge.Samples.Console -- ws://localhost:8080/edgebridge/ blink
```

Available samples:

```bash
dotnet run --project src/EdgeBridge.Samples.Console -- ws://localhost:8080/edgebridge/ blink
dotnet run --project src/EdgeBridge.Samples.Console -- ws://localhost:8080/edgebridge/ button
dotnet run --project src/EdgeBridge.Samples.Console -- ws://localhost:8080/edgebridge/ fade
dotnet run --project src/EdgeBridge.Samples.Console -- ws://localhost:8080/edgebridge/ toy-car
```

The `fade` sample drives an LED through `IPwmOutput` and fades duty cycle up and down on PWM channel `0` by default. Pass a channel number after the sample name to use a different PWM channel:

```bash
dotnet run --project src/EdgeBridge.Samples.Console -- ws://localhost:8080/edgebridge/ fade 1
```

### Avalonia RGB LED sample

The Avalonia desktop sample connects to an EdgeBridge Agent and toggles an RGB LED through three `IDigitalOutput` channels.

```bash
dotnet run --project src/EdgeBridge.Samples.Avalonia
```

The default endpoint is:

```text
ws://localhost:8080/edgebridge/
```

The default GPIO channel mapping is red `17`, green `27`, and blue `22`. Change the channel fields in the UI before connecting if your device uses different GPIO line offsets.

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
    }
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
    "camera": false
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

If that file does not exist, it uses built-in development defaults.

For a device on your network, clients should connect with the device IP or hostname:

```bash
dotnet run --project src/EdgeBridge.Samples.Console -- ws://DEVICE_IP:8080/edgebridge/ blink
```

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

Run a sample:

```bash
dotnet run --project src/EdgeBridge.Samples.Console -- ws://localhost:8080/edgebridge/ blink
dotnet run --project src/EdgeBridge.Samples.Console -- ws://localhost:8080/edgebridge/ fade
```

Stop long-running samples with `Ctrl+C`.

---

## Roadmap

Near-term work:

- add WebSocket authentication and authorization,
- implement WiFi/Bluetooth provisioning,
- add camera stream protocol and transport support,
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
