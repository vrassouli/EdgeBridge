# EdgeBridge

**EdgeBridge** is a hardware abstraction and remote device runtime platform for building applications that can talk to **local**, **remote**, or **simulated** hardware through the same application-facing API.

The core idea is simple:

> Application code should depend on `EdgeBridge.Abstractions`, not on Raspberry Pi, ESP32, Arduino, Jetson, GPIO libraries, transport details, or simulator internals.

That lets the same business/application code run against:

- a local hardware implementation,
- a remote device through an EdgeBridge Agent,
- or a simulator/mock device during development and testing.

The current MVP is mock-first, so it runs immediately on macOS, Linux, and Raspberry Pi without native GPIO dependencies.

---

## Why EdgeBridge?

When building robotics, IoT, home automation, educational hardware projects, or edge-device experiments, application code often becomes tightly coupled to a specific board, GPIO package, protocol, or deployment environment.

EdgeBridge tries to avoid that by separating:

- **what the application wants to do** — read inputs, set outputs, drive motors, read sensors,
- from **where/how the hardware exists** — local board, remote agent, simulator, or future device family.

This makes it easier to:

- develop from a Mac or PC while the hardware is somewhere else,
- run samples before real GPIO support is ready,
- swap transports without changing public APIs,
- add Raspberry Pi, ESP32, Arduino, Jetson, camera, and sensor providers later,
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
- remote client SDK,
- provisioning contracts,
- console samples.

Not implemented yet:

- real Linux GPIO provider,
- authentication/authorization for WebSocket transport,
- reconnect and health reporting in the client SDK,
- real WiFi/Bluetooth provisioning flows,
- camera stream transport,
- full test coverage.

See [`TODO.md`](TODO.md) for the active roadmap.

---

## Repository Layout

```text
.
├── EdgeBridge.slnx
├── README.md
├── TODO.md
├── AGENTS.md
├── docs/
│   ├── linux-agent.md
│   └── adr/
│       └── 0001-mvp-architecture.md
└── src/
    ├── EdgeBridge.Abstractions/
    ├── EdgeBridge.Protocol/
    ├── EdgeBridge.Transport.WebSockets/
    ├── EdgeBridge.Agent/
    ├── EdgeBridge.Client/
    ├── EdgeBridge.Provisioning/
    └── EdgeBridge.Samples.Console/
```

### Projects

| Project | Purpose |
| --- | --- |
| `EdgeBridge.Abstractions` | Public application-facing hardware interfaces and shared device models. |
| `EdgeBridge.Protocol` | Versioned JSON protocol messages, command payloads, errors, and serialization options. |
| `EdgeBridge.Transport.WebSockets` | Replaceable transport contracts plus the current WebSocket implementation. |
| `EdgeBridge.Agent` | Device-side runtime/daemon skeleton. Currently hosts mock hardware. |
| `EdgeBridge.Client` | Remote proxy implementation that exposes `IDevice` over a transport connection. |
| `EdgeBridge.Provisioning` | Contracts for future device setup/provisioning flows such as WiFi/Bluetooth onboarding. |
| `EdgeBridge.Samples.Console` | Console samples for blinking, button watching, and toy-car control. |

---

## Requirements

- .NET 10 SDK
- macOS, Linux, or Raspberry Pi OS

The MVP has no native GPIO dependency, so the Agent can currently run anywhere supported by .NET 10.

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
dotnet run --project src/EdgeBridge.Samples.Console -- ws://localhost:8080/edgebridge/ toy-car
```

---

## Agent Configuration

The Agent can load a JSON config file:

```json
{
  "deviceId": "toy-car-01",
  "deviceName": "Toy Car",
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

Run with:

```bash
dotnet run --project src/EdgeBridge.Agent -- --config=agent.json
```

For a device on your network, clients should connect with the device IP:

```bash
dotnet run --project src/EdgeBridge.Samples.Console -- ws://DEVICE_IP:8080/edgebridge/ blink
```

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

The important part is that application code uses `EdgeBridge.Abstractions`. The implementation can later be local hardware, a remote Agent, or a simulator.

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

See [`docs/linux-agent.md`](docs/linux-agent.md) for copy, install, and `systemd` instructions.

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

For the first accepted architecture decision, see [`docs/adr/0001-mvp-architecture.md`](docs/adr/0001-mvp-architecture.md).

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
```

Stop long-running samples with `Ctrl+C`.

---

## Roadmap

Near-term work:

- add tests for protocol serialization, command correlation, and mock device behavior,
- add graceful unsubscribe support for watch streams,
- add a real Linux GPIO implementation behind `IDevice`,
- add Linux `systemd` packaging,
- add WebSocket authentication and authorization,
- add reconnect policy and connection health reporting,
- implement WiFi/Bluetooth provisioning,
- add camera stream protocol and transport support,
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
