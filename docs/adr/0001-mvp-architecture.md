# ADR 0001: MVP Architecture

## Status

Accepted

## Context

EdgeBridge must let application code interact with local, remote, or simulated hardware without knowing where the hardware lives. The first usable milestone needs to run an Agent on Linux/Raspberry Pi and run samples from a MacBook.

## Decision

The MVP is a .NET 10 solution with separate projects for abstractions, protocol, WebSocket transport, Agent, Client SDK, provisioning contracts, and console samples.

Application code depends on `EdgeBridge.Abstractions`. The Agent hosts a local `IDevice` implementation. The Client SDK exposes remote proxy implementations of the same interfaces. Communication uses versioned JSON protocol messages over WebSockets.

The first Agent hardware implementation is a mock provider. Real GPIO, camera, and device-specific providers will be added behind the same abstractions later.

## Consequences

- Samples can run on macOS immediately.
- The Agent can run on Linux/Raspberry Pi without native GPIO dependencies.
- Transport and hardware providers remain replaceable.
- The protocol can evolve independently from public application-facing abstractions.

