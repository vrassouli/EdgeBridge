# EdgeBridge

EdgeBridge is a hardware abstraction and remote device runtime platform. Application code depends only on `EdgeBridge.Abstractions`, so the same code can run against local hardware, remote hardware through an Agent, or simulator hardware.

## Current MVP

- `EdgeBridge.Abstractions`: async-first hardware interfaces and shared device models.
- `EdgeBridge.Protocol`: versioned JSON protocol messages and command payload DTOs.
- `EdgeBridge.Transport.WebSockets`: replaceable transport contracts plus a WebSocket implementation.
- `EdgeBridge.Agent`: Linux/Raspberry Pi-ready daemon skeleton with mock hardware.
- `EdgeBridge.Client`: remote proxy implementation of the shared abstractions.
- `EdgeBridge.Provisioning`: provisioning contracts for future WiFi/Bluetooth setup.
- `EdgeBridge.Samples.Console`: Mac-friendly console samples.

The first implementation uses mock hardware so it can run on macOS immediately and on Linux/Raspberry Pi without GPIO dependencies.

## Run Locally

Start the Agent:

```bash
dotnet run --project src/EdgeBridge.Agent
```

In another terminal, run a sample:

```bash
dotnet run --project src/EdgeBridge.Samples.Console -- ws://localhost:8080/edgebridge/ blink
```

Available samples:

```bash
dotnet run --project src/EdgeBridge.Samples.Console -- ws://localhost:8080/edgebridge/ blink
dotnet run --project src/EdgeBridge.Samples.Console -- ws://localhost:8080/edgebridge/ button
dotnet run --project src/EdgeBridge.Samples.Console -- ws://localhost:8080/edgebridge/ toy-car
```

## Agent Config

The Agent can load a JSON config:

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

For remote clients, use `ws://device-ip:8080/edgebridge/`.

## Run on Linux/Raspberry Pi

See [docs/linux-agent.md](docs/linux-agent.md) for publish, install, and systemd service instructions.

## Architecture Rules

- Abstractions never depend on implementations.
- Client SDK and Agent expose identical behavior through `EdgeBridge.Abstractions`.
- Public APIs are async-first and accept `CancellationToken`.
- Transport is replaceable.
- Protocol messages are versionable.
- Public names avoid board-specific terminology.
