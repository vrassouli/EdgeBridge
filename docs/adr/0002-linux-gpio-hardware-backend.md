# ADR 0002: Linux GPIO Hardware Backend

## Status

Accepted

## Context

EdgeBridge needs to run against real hardware for Raspberry Pi testing while preserving the central rule that application code depends only on `EdgeBridge.Abstractions`. The same Agent and Client behavior must continue to work with mock, local, remote, and future simulated hardware implementations.

## Decision

The Agent chooses a concrete `IDevice` implementation from configuration. The default backend remains `mock` for local development. A new `linux-gpio` backend lives inside `EdgeBridge.Agent` and uses Linux GPIO chips through `System.Device.Gpio`.

The public abstractions and protocol payloads are unchanged. Channels passed through `IDevice.DigitalInput`, `IDevice.DigitalOutput`, and `IDevice.PwmOutput` are interpreted by this backend as Linux GPIO/PWM line offsets on the configured chip, not as Raspberry Pi-specific names.

## Consequences

- Raspberry Pi and other Linux devices can be tested with real GPIO behind the existing `IDevice` contract.
- macOS and CI-like environments can keep using the mock backend without native GPIO dependencies.
- Board-specific pin naming stays out of public APIs.
- Future hardware providers can be added by extending Agent configuration and implementing `IDevice`, not by changing application code.
