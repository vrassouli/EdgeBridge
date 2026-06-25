# ADR 0003: Primary GUI Sample and Agent Config Updates

## Status

Accepted

## Context

EdgeBridge needs a sample that demonstrates local, remote, and simulated hardware through the same abstractions without feeling like a toy demo. The sample also needs to manage devices, feature-specific channel configuration, and Agent configuration in one place.

## Decision

The Avalonia sample is the primary sample surface. It uses a professional all-in-one management UI with local JSON profile persistence, dynamic feature pages for implemented capabilities, and shared desktop/mobile view structure.

Agent configuration is exposed through versioned protocol commands:

- `device.config.get`
- `device.config.update`

Updates are durable and return `restartRequired: true` in v1. The Agent persists the new config, but runtime backend, transport, chip, module, I2C, camera, and motor mapping changes take effect after Agent restart.

The console sample is removed to keep the sample story focused.

## Consequences

- The GUI becomes the default place to exercise GPIO, PWM, motors, I2C register access, camera control/status, and Agent config updates.
- Application-facing hardware code still depends on `EdgeBridge.Abstractions`.
- Operational config management remains client/Agent behavior rather than a hardware abstraction.
- Android support is represented by a separate host project so default solution builds do not require optional Android workloads.
